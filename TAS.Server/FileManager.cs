﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TAS.Remoting.Server;
using TAS.Common.Interfaces;
using TAS.Server.Media;
using TAS.Common;
using TAS.Common.Interfaces.Media;
using TAS.Common.Interfaces.MediaDirectory;
using static System.Threading.Thread;

namespace TAS.Server
{
    public class FileManager: DtoBase, IFileManager
    {
#pragma warning disable CS0169
        [JsonProperty]
        private readonly string Dummy; // at  least one property should be serialized to resolve references
#pragma warning restore
        private static readonly NLog.Logger Logger = NLog.LogManager.GetLogger(nameof(FileManager));
        private readonly SynchronizedCollection<IFileOperation> _queueSimpleOperation = new SynchronizedCollection<IFileOperation>();
        private readonly SynchronizedCollection<IFileOperation> _queueConvertOperation = new SynchronizedCollection<IFileOperation>();
        private readonly SynchronizedCollection<IFileOperation> _queueExportOperation = new SynchronizedCollection<IFileOperation>();
        private bool _isRunningSimpleOperation;
        private bool _isRunningConvertOperation;
        private bool _isRunningExportOperation;
        internal readonly TempDirectory TempDirectory;
        internal double ReferenceLoudness;

        internal FileManager(TempDirectory tempDirectory)
        {
            TempDirectory = tempDirectory;
        }
        
        public event EventHandler<FileOperationEventArgs> OperationAdded;
        public event EventHandler<FileOperationEventArgs> OperationCompleted;

        public IIngestOperation CreateIngestOperation(IIngestMedia sourceMedia, IMediaManager destMediaManager)
        {
            if (!(sourceMedia.Directory is IIngestDirectory sourceDirectory))
                return null;
            var pri = destMediaManager.MediaDirectoryPRI;
            var sec = destMediaManager.MediaDirectorySEC;
            if (!((pri != null && pri.DirectoryExists() ? pri : sec != null && sec.DirectoryExists() ? sec : null) is ServerDirectory dir))
                return null;
            
            return new IngestOperation(this)
            {
                Source = sourceMedia,
                DestDirectory = dir,
                AudioVolume = sourceDirectory.AudioVolume,
                SourceFieldOrderEnforceConversion = sourceDirectory.SourceFieldOrder,
                AspectConversion = sourceDirectory.AspectConversion,
                LoudnessCheck = sourceDirectory.MediaLoudnessCheckAfterIngest,
                StartTC = sourceMedia.TcStart,
                Duration = sourceMedia.Duration,
                MovieContainerFormat = dir.Server.MovieContainerFormat
            };
        }
        public ILoudnessOperation CreateLoudnessOperation()
        {
            return new LoudnessOperation(this);
        }
        public IFileOperation CreateSimpleOperation() { return new FileOperation(this); }
        
        public IEnumerable<IFileOperation> GetOperationQueue()
        {
            List<IFileOperation> retList;
            lock (_queueSimpleOperation.SyncRoot)
                retList = new List<IFileOperation>(_queueSimpleOperation);
            lock (_queueConvertOperation.SyncRoot)
                retList.AddRange(_queueConvertOperation);
            lock (_queueExportOperation.SyncRoot)
                retList.AddRange(_queueExportOperation);
            return retList;
        }

        public void QueueList(IEnumerable<IFileOperation> operationList, bool toTop = false)
        {
            foreach (var operation in operationList)
                Queue(operation, toTop);
        }

        public void Queue(IFileOperation operation, bool toTop = false)
        {
            if (operation is FileOperation op)
                _queue(op, toTop);
        }

        public void CancelPending()
        {
            lock (_queueSimpleOperation.SyncRoot)
                _queueSimpleOperation.Where(op => op.OperationStatus == FileOperationStatus.Waiting).ToList().ForEach(op => op.Abort());
            lock (_queueConvertOperation.SyncRoot)
                _queueConvertOperation.Where(op => op.OperationStatus == FileOperationStatus.Waiting).ToList().ForEach(op => op.Abort());
            lock (_queueExportOperation.SyncRoot)
                _queueExportOperation.Where(op => op.OperationStatus == FileOperationStatus.Waiting).ToList().ForEach(op => op.Abort());
            Logger.Trace("Cancelled pending operations");
        }

        private async void _queue(FileOperation operation, bool toTop)
        {
            operation.ScheduledTime = DateTime.UtcNow;
            operation.OperationStatus = FileOperationStatus.Waiting;
            Logger.Info("Operation scheduled: {0}", operation);
            NotifyOperation(OperationAdded, operation);

            if ((operation.Kind == TFileOperationKind.Copy || operation.Kind == TFileOperationKind.Move || operation.Kind == TFileOperationKind.Ingest))
            {
                IMedia destMedia = operation.Dest;
                if (destMedia != null)
                    destMedia.MediaStatus = TMediaStatus.CopyPending;
            }
            if (operation.Kind == TFileOperationKind.Ingest)
            {
                    if (toTop)
                        _queueConvertOperation.Insert(0, operation);
                    else
                        _queueConvertOperation.Add(operation);
                    if (!_isRunningConvertOperation)
                    {
                        _isRunningConvertOperation = true;
                        await _runOperation(_queueConvertOperation);
                    }
            }
            if (operation.Kind == TFileOperationKind.Export)
            {
                    if (toTop)
                        _queueExportOperation.Insert(0, operation);
                    else
                        _queueExportOperation.Add(operation);
                    if (!_isRunningExportOperation)
                    {
                        _isRunningExportOperation = true;
                        await _runOperation(_queueExportOperation);
                }
            }
            if (operation.Kind == TFileOperationKind.Copy
                || operation.Kind == TFileOperationKind.Delete
                || operation.Kind == TFileOperationKind.Loudness
                || operation.Kind == TFileOperationKind.Move)
            {
                    if (toTop)
                        _queueSimpleOperation.Insert(0, operation);
                    else
                        _queueSimpleOperation.Add(operation);
                    if (!_isRunningSimpleOperation)
                    {
                        _isRunningSimpleOperation = true;
                        await _runOperation(_queueSimpleOperation);
                    }
            }
        }

        private async Task _runOperation(SynchronizedCollection<IFileOperation> queue)
        {
            FileOperation op;
            lock (queue.SyncRoot)
                op = queue.FirstOrDefault() as FileOperation;
            while (op != null)
            {
                try
                {
                    queue.Remove(op);
                    if (!op.IsAborted)
                    {
                        if (await op.Execute())
                        {
                            NotifyOperation(OperationCompleted, op);
                            op.Dispose();
                        }
                        else
                        {
                            if (op.TryCount > 0)
                            {
                                Sleep(500);
                                queue.Add(op);
                            }
                            else
                            {
                                op.Fail();
                                NotifyOperation(OperationCompleted, op);
                                if (op.Dest?.FileExists() == true)
                                    op.Dest.Delete();
                                op.Dispose();
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, "RunOperation exception");
                }
                lock (queue.SyncRoot)
                    op = queue.FirstOrDefault() as FileOperation;
            }
        }

        private void NotifyOperation(EventHandler<FileOperationEventArgs> handler, IFileOperation operation)
        {
            handler?.Invoke(this, new FileOperationEventArgs(operation));
        }
    }


}
