using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

using Rainmeter;

namespace Rainnet
{
    public class DataMeasure : Measure
    {
        private static readonly string SessionId = "rainnet-datameasure-";
        private static List<DataMeasure> all = new List<DataMeasure>();
        private static int SessionCount = 0;

        private readonly IntPtr rainmeter;
        private int count = 5;
        private SessionProperty sort = SessionProperty.Speed;
        private bool sortAscending = false;
        private readonly string sessionName = "";
        private TraceEventSession session = null;
        private Dictionary<int, DownloadSession> sessionData = new Dictionary<int, DownloadSession>();

        public DataMeasure(IntPtr rainmeter)
        {
            this.rainmeter = rainmeter;

            sessionName = SessionId + SessionCount++;
            all.Add(this);
        }

        public override void Finished()
        {
            session?.Dispose();
            all.Remove(this);
        }

        public override void Reload(API api, ref double maxValue)
        {
            base.Reload(api, ref maxValue);

            sort = ReadSessionProperty(api, "Sort");

            sortAscending = api.ReadInt("SortAscending", 0) == 1;

            count = Math.Max(0, api.ReadInt("Count", 5));

            if (!(TraceEventSession.IsElevated() ?? false))
            {
                API.Log(rainmeter, API.LogType.Error, "Rainnet.dll: Not elevated");
                return;
            }

            session?.Dispose();
            session = new TraceEventSession(sessionName);

            Dictionary<int, DownloadSession> data = new Dictionary<int, DownloadSession>();
            session.Source.Dynamic.All += ProcessEvent;

            session.EnableProvider("Microsoft-Windows-Kernel-Network");
            Task.Run(() => session.Source.Process());
        }

        private void ProcessEvent(TraceEvent e)
        {
            int sizeIndex = e.PayloadIndex("size");
            if (sizeIndex >= 0)
            {
                try
                {
                    int pidIndex = e.PayloadIndex("PID");
                    int pid = (int)e.PayloadValue(pidIndex);
                    int bytes = (int)e.PayloadValue(sizeIndex);
                    lock (sessionData)
                    {
                        if (sessionData.TryGetValue(pid, out DownloadSession info))
                            info.AddTotal(bytes);
                        else
                            sessionData[pid] = new DownloadSession(pid, (int)e.PayloadValue(sizeIndex));
                    }
                }
                catch { }
            }
        }

        public override double Update()
        {
            try
            {
                IEnumerable<DownloadSession> updated = sessionData.Values;

                if (sort == SessionProperty.Name)
                {
                    if (sortAscending)
                        updated = updated.OrderBy(x => x.ProcessName);
                    else
                        updated = updated.OrderByDescending(x => x.ProcessName);
                }
                else if (sort == SessionProperty.Total)
                {
                    if (sortAscending)
                        updated = updated.OrderBy(x => x.Total);
                    else
                        updated = updated.OrderByDescending(x => x.Total);
                }
                else if (sort == SessionProperty.PID)
                {
                    if (sortAscending)
                        updated = updated.OrderBy(x => x.PID);
                    else
                        updated = updated.OrderByDescending(x => x.PID);
                }
                else
                {
                    if (sortAscending)
                        updated = updated.OrderBy(x => x.Difference);
                    else
                        updated = updated.OrderByDescending(x => x.Difference);
                }

                if (count > 0)
                    updated = updated.Take(count);

                lock (sessionData)
                {
                    Data = updated.ToArray();

                    foreach (DownloadSession datum in sessionData.Values)
                        datum.Update();
                }
            }
            catch { }

            return base.Update();
        }

        public static DataMeasure GetMeasure(string name, IntPtr skin)
        {
            return all
                .Where(x => x.Name == name && x.Skin == skin)
                .FirstOrDefault();
        }

        public static SessionProperty ReadSessionProperty(API api, string propertyName)
        {
            string sort = api.ReadString(propertyName, "");
            switch (sort.ToLower())
            {
                case "name":
                    return SessionProperty.Name;
                case "total":
                    return SessionProperty.Total;
                case "pid":
                    return SessionProperty.PID;
                default:
                    return SessionProperty.Speed;
            }
        }

        // ----- Properties ----- //

        public IEnumerable<DownloadSession> Data { get; private set; }
    }
}
