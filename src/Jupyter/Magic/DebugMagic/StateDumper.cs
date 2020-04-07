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

    internal class StateDumper : QuantumSimulator.StateDumper
    {
        private List<Complex> amplitudes = new List<Complex>();

        public StateDumper(QuantumSimulator simulator) : base(simulator)
        {
        }

        public override bool Callback(uint index, double real, double imaginary)
        {
            amplitudes.Add(new Complex(real, imaginary));
            return true;
        }

        public override bool Dump(IQArray<Qubit>? qubits = null)
        {
            amplitudes = new List<Complex>();
            return base.Dump(qubits);
        }

        public Complex[] GetAmplitudes() => amplitudes.ToArray();

        public Complex[] DumpAndGetAmplitudes(IQArray<Qubit>? qubits = null)
        {
            Dump(qubits);
            return GetAmplitudes();
        }
    }
}

