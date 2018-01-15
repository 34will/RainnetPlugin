using System.Collections.Generic;
using System.Linq;

using Rainmeter;

namespace Rainnet
{
    public class OutputMeasure : Measure
    {
        public static readonly string ParentParameter = "DataMeasure";

        private DataMeasure parent = null;
        private SessionProperty property = SessionProperty.Speed;
        private string contents = "";

        public override void Reload(API api, ref double maxValue)
        {
            base.Reload(api, ref maxValue);

            string parentName = api.ReadString(ParentParameter, "");

            parent = DataMeasure.GetMeasure(parentName, Skin);
            if (parent == null)
                API.Log(API.LogType.Error, $"Rainnet.dll: {ParentParameter} = {parentName} not valid");

            property = DataMeasure.ReadSessionProperty(api, "Property");
        }

        public override double Update()
        {
            if (parent != null)
            {
                IEnumerable<string> properties;

                if (property == SessionProperty.Name)
                    properties = parent.Data.Select(x => x.ProcessName);
                else if (property == SessionProperty.Total)
                    properties = parent.Data.Select(x => FormatBytes(x.Total));
                else if (property == SessionProperty.PID)
                    properties = parent.Data.Select(x => x.PID.ToString());
                else
                    properties = parent.Data.Select(x => FormatBytes(x.LastDifference, 1, "/s"));

                contents = string.Join("\n", properties);
            }

            return base.Update();
        }

        public override string GetString()
        {
            return contents;
        }

        private static string FormatBytes(long amount, int decimalPoints = 2, string append = "")
        {
            string dp = "";
            if (decimalPoints > 0)
            {
                dp = ":0.";
                for (int i = 0; i < decimalPoints; i++)
                    dp += "0";
            }

            if (amount <= 1000)
                return string.Format("{0" + dp + "}  B{1}", (float)amount, append);
            else if (amount <= 1000000)
                return string.Format("{0" + dp + "} KB{1}", (float)amount / 1000.0f, append);
            else if (amount <= 1000000000)
                return string.Format("{0" + dp + "} MB{1}", (float)amount / 1000000.0f, append);
            else if (amount <= 1000000000000)
                return string.Format("{0" + dp + "} GB{1}", (float)amount / 1000000000.0f, append);
            else
                return string.Format("{0" + dp + "} TB{1}", (float)amount / 1000000000000.0f, append);
        }
    }
}
