using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TraceEventSamples.Producer;

public class DownloadSession
{
    private long oldTotal = 0;

    public DownloadSession(int PID, long initialTotal)
    {
        this.PID = PID;
        ProcessName = Process.GetProcessById(PID).ProcessName;
        Total = initialTotal;
    }

    public void AddTotal(long data)
    {
        Total += data;
    }

    public void Update()
    {
        oldTotal = Total;
    }

    public static string FormatBytes(long amount, int decimalPoints = 2, string append = "")
    {
        string dp = "";
        if (decimalPoints > 0)
        {
            dp = ":0.";
            for (int i = 0; i < decimalPoints; i++)
                dp += "0";
        }

        if (amount <= 1000)
            return String.Format("{0" + dp + "}B{1}", (float)amount, append);
        else if (amount <= 1000000)
            return String.Format("{0" + dp + "}KB{1}", (float)amount / 1000.0f, append);
        else if (amount <= 1000000000)
            return String.Format("{0" + dp + "}MB{1}", (float)amount / 1000000.0f, append);
        else if (amount <= 1000000000000)
            return String.Format("{0" + dp + "}GB{1}", (float)amount / 1000000000.0f, append);
        else
            return String.Format("{0" + dp + "}TB{1}", (float)amount / 1000000000000.0f, append);
    }

    // ----- Properties ----- //

    public int PID { get; private set; }

    public string ProcessName { get; private set; }

    public long Total { get; private set; }

    public long Difference { get { return Total - oldTotal; } }
}

/* README FIRST */
// This program is a simple demo that demonstrates the RegisteredTraceEventParser to look at OS events
// in real time.   Now many of the events that you might be interested in are in the Kernel event
// source (see 3*_Kernel*) samples where you sue the KernelTraceEventParser.  Please see the 
// TraceEvent Programmers guide (In the TraceEvent NuGet package or one the web) for more 
//
// The basic flow of the program is
//     * Create a 'real time' TraceEventSession.  This lets you control what events you wish to collect
//     * Connect a ETWTraceEventSource for the session using the 'Source' property.   A TraceEventSource 
//           represents the stream of events coming from the session.  
//     * Connect a TraceEventParser to the source.    Parsers know how to interpret events from particular 
//          ETW providers.  In this case we care about non-kernel OS events and the RegisteredTraceEventParser
//          is the parser that knows how to decode the events.   
//     * Attach callbacks to the Parser for the events you are interested in.  We use the 'All' C# event 
//          for this.  
//     * Enable the ETW providers of interest (in this case Microsoft-Windows-Kernel-File provider) despite
//          its name it is NOT part of the kernel provider, but is a 'normal' user mode provider that 
//          supplies much of the same information as FILE keyword on the kernel provider.   
//     * Call the ETWTraceEventSource.Process() method, which waits for events and sends them to the callbacks. 
//     * At this point callbacks get called when the events come in.  
//     
// After 'Process()' is called, it will return if
//     * The TraceEventSession.Dispose() is called.  
namespace TraceEventSamples
{
    /// <summary>
    /// The main program is the 'listener' that listens and processes the events that come from ANY 
    /// process that is generating Microsoft-Demos-SimpleMonitor events.  
    /// </summary>
    class SimpleOSEventMonitor
    {
        static TextWriter Out = AllSamples.Out;

        /// <summary>
        /// This is a demo of using TraceEvent to activate a 'real time' provider that is listening to 
        /// the MyEventSource above.   Normally this event source would be in a different process,  but 
        /// it also works if this process generate the events and I do that here for simplicity.  
        /// </summary>
        public static int Run()
        {
            // Today you have to be Admin to turn on ETW events (anyone can write ETW events).   
            if (!(TraceEventSession.IsElevated() ?? false))
            {
                Console.WriteLine("To turn on ETW events you need to be Administrator, please run from an Admin process.");
                Debugger.Break();
                return -1;
            }

            // To listen to ETW events you need a session, which allows you to control which events will be produced
            // Note that it is the session and not the source that buffers events, and by default sessions will buffer
            // 64MB of events before dropping events.  Thus even if you don't immediately connect up the source and
            // read the events you should not lose them. 
            //
            // As mentioned below, sessions can outlive the process that created them.  Thus you may need a way of 
            // naming the session so that you can 'reconnect' to it from another process.   This is what the name
            // is for.  It can be anything, but it should be descriptive and unique.   If you expect multiple versions
            // of your program to run simultaneously, you need to generate unique names (e.g. add a process ID suffix) 
            // however this is dangerous because you can leave data collection on if the program ends unexpectedly.  
            var sessionName = "SimpleMontitorSession";
            using (var session = new TraceEventSession(sessionName))
            {
                /* BY DEFAULT ETW SESSIONS SURVIVE THE DEATH OF THE PROESS THAT CREATES THEM! */
                // Unlike most other resources on the system, ETW session live beyond the lifetime of the 
                // process that created them.   This is very useful in some scenarios, but also creates the 
                // very real possibility of leaving 'orphan' sessions running.  
                //
                // To help avoid this by default TraceEventSession sets 'StopOnDispose' so that it will stop
                // the ETW session if the TraceEventSession dies.   Thus executions that 'clean up' the TraceEventSession
                // will clean up the ETW session.   This covers many cases (including throwing exceptions)
                //  
                // However if the process is killed manually (including control C) this cleanup will not happen.  
                // Thus best practices include
                //
                //     * Add a Control C handler that calls session.Dispose() so it gets cleaned up in this common case
                //     * use the same session name (say your program name) run-to-run so you don't create many orphans. 
                //
                // By default TraceEventSessions are in 'create' mode where it assumes you want to create a new session.
                // In this mode if a session already exists, it is stopped and the new one is created.   
                // 
                // Here we install the Control C handler.   It is OK if Dispose is called more than once.  
                Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e) { session.Dispose(); };

                /*****************************************************************************************************/
                // Hook up events.   To so this first we need a 'Parser. which knows how to part the events of a particular Event Provider.
                // In this case we get a DynamicTraceEventSource, which knows how to parse any EventSource provider.    This parser
                // is so common, that TraceEventSource as a shortcut property called 'Dynamic' that fetches this parsers.  

                // For debugging, and demo purposes, hook up a callback for every event that 'Dynamic' knows about (this is not EVERY
                // event only those know about by RegisteredTraceEventParser).   However the 'UnhandledEvents' handler below will catch
                // the other ones.
                Dictionary<int, DownloadSession> data = new Dictionary<int, DownloadSession>();
                session.Source.Dynamic.All += delegate (TraceEvent e)
                {
                    // ETW buffers events and only delivers them after buffering up for some amount of time.  Thus 
                    // there is a small delay of about 2-4 seconds between the timestamp on the event (which is very 
                    // accurate), and the time we actually get the event.  We measure that delay here.
                    int sizeIndex = e.PayloadIndex("size");
                    if (sizeIndex >= 0)
                    {
                        try
                        {
                            int pidIndex = e.PayloadIndex("PID");
                            int pid = (int)e.PayloadValue(pidIndex);
                            int bytes = (int)e.PayloadValue(sizeIndex);
                            lock (data)
                            {
                                if (data.TryGetValue(pid, out DownloadSession info))
                                    info.AddTotal(bytes);
                                else
                                    data[pid] = new DownloadSession(pid, (int)e.PayloadValue(sizeIndex));
                            }
                        }
                        catch { }
                    }
                };

                bool running = true;
                Task.Run(async () =>
                {
                    while (running)
                    {
                        try
                        {
                            await Task.Delay(1000);
                            lock (data)
                            {
                                Console.Clear();
                                Console.SetCursorPosition(0, 0);
                                Console.WriteLine("{0,-6} {1,11} {2,11} {3,-30}", "PID", "Total Bytes", "Bytes/sec", "Name");

                                foreach (var datum in data.OrderByDescending(x => x.Value.Difference).Take(5))
                                {
                                    Console.WriteLine("{0,-6} {1,11} {2,11} {3,-30}", datum.Value.PID, DownloadSession.FormatBytes(datum.Value.Total), DownloadSession.FormatBytes(datum.Value.Difference, 1, "/s"), datum.Value.ProcessName);
                                    datum.Value.Update();
                                }
                            }
                        }
                        catch { }
                    }
                });

                // At this point we have created a TraceEventSession, hooked it up to a TraceEventSource, and hooked the
                // TraceEventSource to a TraceEventParser (you can do several of these), and then hooked up callbacks
                // up to the TraceEventParser (again you can have several).  However we have NOT actually told any
                // provider (EventSources) to actually send any events to our TraceEventSession.  
                // We do that now.  

                // Enable my provider, you can call many of these on the same session to get events from other providers.  
                // Because this EventSource did not define any keywords, I can only turn on all events or none.  
                var restarted = session.EnableProvider("Microsoft-Windows-Kernel-Network");
                if (restarted)      // Generally you don't bother with this warning, but for the demo we do. 
                    Console.WriteLine("The session {0} was already active, it has been restarted.", sessionName);

                Console.Clear();
                Console.SetCursorPosition(0, 0);
                Console.WriteLine("{0,-6} {1,11} {2,11} {3,-30}", "PID", "Total Bytes", "Bytes/sec", "Name");

                // go into a loop processing events can calling the callbacks.  Because this is live data (not from a file)
                // processing never completes by itself, but only because someone called 'source.Dispose()'.  
                session.Source.Process();

                running = false;
                Console.WriteLine();
                Console.WriteLine("Stopping the collection of events.");
                Console.ReadLine();
            }
            return 0;
        }
    }
}