﻿using System;
using TAS.Common;
using TAS.Server.Common;

namespace TAS.Server.Interfaces
{
    public interface IEventProperties
    {
        decimal? AudioVolume { get; set; }
        TimeSpan Duration { get; set; }
        bool Enabled { get; set; }
        string EventName { get; set; }
        TEventType EventType { get; set; }
        bool Hold { get; set; }
        string IdAux { get; set; }
        ulong idProgramme { get; set; }
        VideoLayer Layer { get; set; }
        Guid MediaGuid { get; }
        TPlayState PlayState { get; set; }
        long Position { get; set; }
        TimeSpan? RequestedStartTime { get; set; }
        TimeSpan ScheduledDelay { get; set; }
        TimeSpan ScheduledTC { get; set; }
        DateTime ScheduledTime { get; set; }
        TimeSpan StartTC { get; set; }
        DateTime StartTime { get; }
        TStartType StartType { get; set; }
        TimeSpan TransitionTime { get; set; }
        TTransitionType TransitionType { get; set; }
        EventGPI GPI { get; set; }
    }
}