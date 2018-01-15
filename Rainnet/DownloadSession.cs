using System.Diagnostics;

namespace Rainnet
{
    public enum SessionProperty
    {
        Speed = 0,
        Total,
        Name,
        PID
    }

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
            LastDifference = Difference;
            oldTotal = Total;
        }

        // ----- Properties ----- //

        public int PID { get; private set; }

        public string ProcessName { get; private set; }

        public long Total { get; private set; }

        public long Difference { get { return Total - oldTotal; } }

        public long LastDifference { get; private set; }
    }
}
