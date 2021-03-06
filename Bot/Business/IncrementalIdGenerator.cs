﻿using System;
using System.Threading;
using Helix.Bot.Abstractions;

namespace Helix.Bot
{
    public class IncrementalIdGenerator : IIncrementalIdGenerator
    {
        int _currentId;

        [Obsolete(ErrorMessage.UseDependencyInjection, true)]
        public IncrementalIdGenerator()
        {
            /* Do nothing */
        }

        public int GetNext() { return Interlocked.Increment(ref _currentId); }
    }
}