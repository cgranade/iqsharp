// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Jupyter.Core;
using Microsoft.Jupyter.Core.Protocol;
using Microsoft.Quantum.IQSharp;
using Microsoft.Quantum.IQSharp.Common;
using Microsoft.Quantum.IQSharp.Jupyter;
using Microsoft.Quantum.Simulation.Core;
using Microsoft.Quantum.Simulation.Simulators;
using Newtonsoft.Json;

namespace Microsoft.Quantum.IQSharp.Jupyter
{

    [JsonObject(MemberSerialization.OptIn)]
    internal class OperationEventContent : MessageContent
    {
        [JsonProperty("debug_session")]
        public string? SessionId { get; set; }

        [JsonProperty("callable_name")]
        public string? CallableName { get; set; }

        [JsonProperty("input")]
        public string? CallableInput { get; set; }

        [JsonProperty("state")]
        public Complex[]? State { get; set; }
    }

}
