﻿using JetBrains.Annotations;

namespace Helix.IPC.Abstractions
{
    public class Message
    {
        public string Payload { get; set; }

        public string Text { get; [UsedImplicitly] set; }
    }
}