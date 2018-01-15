using System;
using System.Runtime.InteropServices;

using Rainmeter;

namespace Rainnet
{
    public static class Plugin
    {
        static IntPtr StringBuffer = IntPtr.Zero;

        [DllExport]
        public static void Initialize(ref IntPtr data, IntPtr rm)
        {
            API api = new API(rm);

            string parent = api.ReadString(OutputMeasure.ParentParameter, "");
            Measure measure;
            if (String.IsNullOrEmpty(parent))
                measure = new DataMeasure();
            else
                measure = new OutputMeasure();

            data = GCHandle.ToIntPtr(GCHandle.Alloc(measure));
        }

        [DllExport]
        public static void Finalize(IntPtr data)
        {
            GCHandle handle = GCHandle.FromIntPtr(data);
            ((Measure)GCHandle.FromIntPtr(data).Target).Finished();
            handle.Free();

            if (StringBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(StringBuffer);
                StringBuffer = IntPtr.Zero;
            }
        }

        [DllExport]
        public static void Reload(IntPtr data, IntPtr rm, ref double maxValue)
        {
            ((Measure)GCHandle.FromIntPtr(data).Target).Reload(new API(rm), ref maxValue);
        }

        [DllExport]
        public static double Update(IntPtr data)
        {
            return ((Measure)GCHandle.FromIntPtr(data).Target).Update();
        }

        [DllExport]
        public static IntPtr GetString(IntPtr data)
        {
            if (StringBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(StringBuffer);
                StringBuffer = IntPtr.Zero;
            }

            string stringValue = ((Measure)GCHandle.FromIntPtr(data).Target).GetString();
            if (stringValue != null)
            {
                StringBuffer = Marshal.StringToHGlobalUni(stringValue);
            }

            return StringBuffer;
        }
    }
}
