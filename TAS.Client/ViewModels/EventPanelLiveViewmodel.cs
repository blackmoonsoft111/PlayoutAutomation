﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TAS.Server.Interfaces;

namespace TAS.Client.ViewModels
{
    public class EventPanelLiveViewmodel: EventPanelRundownElementViewmodelBase
    {
        public EventPanelLiveViewmodel(IEvent ev, EventPanelViewmodelBase parent) : base(ev, parent) { }
    }
}
