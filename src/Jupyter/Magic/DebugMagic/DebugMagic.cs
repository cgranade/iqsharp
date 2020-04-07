// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

    public class DebugMagic : AbstractMagic
    {
        public string ProtocolVersion { get => "5.2.0"; }
        public DebugMagic(
                ISymbolResolver resolver, IConfigurationSource configurationSource, IShellRouter router, IShellServer shellServer,
                ILogger<DebugMagic> logger
        ) : base(
            "debug",
            new Documentation {
                Summary = "TODO"
            })
        {
            this.SymbolResolver = resolver;
            this.ConfigurationSource = configurationSource;
            this.shellServer = shellServer;
            this.logger = logger;
            router.RegisterHandler("iqsharp_debug_request", this.HandleStartMessage);
            router.RegisterHandler("iqsharp_debug_advance", this.HandleAdvanceMessage);
        }

        private ConcurrentDictionary<Guid, ManualResetEvent> sessionAdvanceEvents
            = new ConcurrentDictionary<Guid, ManualResetEvent>();
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private IShellServer shellServer;
        private ILogger<DebugMagic> logger;

        /// <summary>
        ///      The symbol resolver used by this magic command to find
        ///      operations or functions to be simulated.
        /// </summary>
        public ISymbolResolver SymbolResolver { get; }

        /// <summary>
        ///     The configuration source used by this magic command to control
        ///     simulation options (e.g.: dump formatting options).
        /// </summary>
        public IConfigurationSource ConfigurationSource { get; }

        /// <inheritdoc />
        public override async Task<ExecutionResult> Run(string input, IChannel channel)
        {
            // Run the async task in a new thread so that we don't block the
            // server thread.
            return await Task.Run(() => RunAsync(input, channel));
        }

        private async Task HandleStartMessage(Message message)
        {
            var content = (message.Content as UnknownContent);
            var session = content?.Data?["debug_session"];
            if (session == null)
            {
                // TODO: Communicate error back here!
            }
            else
            {
                shellServer.SendShellMessage(
                    new Message
                    {
                        Header = new MessageHeader
                        {
                            MessageType = "iqsharp_debug_reply"
                        },
                        Content = new UnknownContent
                        {
                            Data = new Dictionary<string, object>
                            {
                                ["debug_session"] = session
                            }
                        }
                    }
                    .AsReplyTo(message)
                );
            }
        }

        private async Task HandleAdvanceMessage(Message message)
        {
            logger.LogDebug("Got debug advance message:", message);
            var content = (message.Content as UnknownContent);
            var session = content?.Data?["debug_session"];
            if (session == null)
            {
                logger.LogWarning("Got debug advance message, but debug_session was null.", message);
            }
            else
            {
                var sessionGuid = Guid.Parse(session.ToString());
                ManualResetEvent? @event = null;
                lock (sessionAdvanceEvents)
                {
                    @event = sessionAdvanceEvents[sessionGuid];
                }
                @event.Set();
            }
        }

        private async Task WaitForAdvance(Guid session)
        {
            await Task.Run(() =>
            {
                ManualResetEvent? @event = null;
                // Find the event we need to wait on.
                lock (sessionAdvanceEvents)
                {
                    @event = sessionAdvanceEvents[session];
                }
                @event.Reset();
                @event.WaitOne();
            }, cancellationTokenSource.Token);
        }

        private Action<ICallable, IApplyData> OperationEventHandler(
                string messageType, Guid session,
                StateDumper dumper, Action? then = null
        ) =>
            (callable, args) =>
            {
                // Tell the IOPub channel about the operation event.
                shellServer.SendIoPubMessage(
                    new Message
                    {
                        Header = new MessageHeader
                        {
                            MessageType = messageType
                        },
                        Content = new OperationEventContent
                        {
                            SessionId = session.ToString(),
                            CallableName = callable.FullName,
                            CallableInput = args.ToString(), // TODO: encode this as well!
                            State = dumper.DumpAndGetAmplitudes()
                        }
                    }
                );
                then?.Invoke();
                WaitForAdvance(session).Wait();
            };

        /// <summary>
        ///     Simulates an operation given a string with its name and a JSON
        ///     encoding of its arguments.
        /// </summary>
        public async Task<ExecutionResult> RunAsync(string input, IChannel channel)
        {
            var (name, args) = ParseInput(input);

            var symbol = SymbolResolver.Resolve(name) as IQSharpSymbol;
            if (symbol == null) throw new InvalidOperationException($"Invalid operation name: {name}");

            var session = Guid.NewGuid();
            lock (sessionAdvanceEvents)
            {
                sessionAdvanceEvents[session] = new ManualResetEvent(true);
            }
            using var qsim = new QuantumSimulator();
            qsim.DisableLogToConsole();
            qsim.OnLog += channel.Stdout;
            var dumper = new StateDumper(qsim);

            // Make something to display the simulator state.
            var stateUpdater = channel.DisplayUpdatable("");

            void UpdateState()
            {
                var state = new DisplayableState
                {
                    NQubits = (int)qsim.QubitManager.GetAllocatedQubitsCount(),
                    QubitIds = qsim.QubitIds.Cast<int>().ToList(),
                    Amplitudes = dumper.DumpAndGetAmplitudes()
                };
                stateUpdater.Update(state);
            }
            
            qsim.OnOperationStart += OperationEventHandler("iqsharp_debug_opstart", session, dumper, UpdateState);
            qsim.OnOperationEnd += OperationEventHandler("iqsharp_debug_opend", session, dumper, UpdateState);


            channel.Display(new RawHtmlPayload
            {
                Value = $@"
                    <a href=""#"" id=""iqsharp-next-{session}"">Next</a>
                    <span>
                        <strong>Current callable: </strong>
                        <span id=""iqsharp-current-{session}""></span>
                    </span>
                    <script type=""text/javascript""><![CDATA[
                        IQSharp.start_debug(""{session}"");
                    ]]></script>
                "
            });
            var value = await Task.Run(() => symbol.Operation.RunAsync(qsim, args));
            return value.ToExecutionResult();
        }
    }
}
