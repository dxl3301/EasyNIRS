using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;

namespace fNIRStreamingApp
{
    public class dataBinder : Binder, IGetTimestamp
    {
        public dataBinder(BTClassicFNIR service)
        {
            this.Service = service;
        }

        public dataBinder Service { get; private set; }

        public string GetFormattedTimestamp()
        {
            return Service?.GetFormattedTimestamp();
        }
    }
}