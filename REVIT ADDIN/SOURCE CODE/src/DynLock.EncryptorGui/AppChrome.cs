using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace DynLock.EncryptorGui
{
    /// <summary>
    /// Shared window icon (BIMLab "B") for every Studio form. Loaded once from the
    /// embedded BimDynamo.ico using the same PNG-frame extraction as Program.cs, so
    /// it works on .NET Framework where <c>new Icon(stream)</c> can choke on
    /// PNG-compressed icon frames. Assign it to <c>Form.Icon</c> in each form's
    /// constructor - child dialogs do not inherit the icon Program.cs sets on the
    /// login/main windows.
    /// </summary>
    internal static class AppChrome
    {
        private static readonly Icon _icon = Build();

        /// <summary>App/window icon, or <c>null</c> if the resource is missing.</summary>
        internal static Icon Icon => _icon;

        private static Icon Build()
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("DynLock.EncryptorGui.Resources.BimDynamo.ico"))
                {
                    if (s == null)
                        return null;

                    var raw = new byte[s.Length];
                    s.Read(raw, 0, raw.Length);

                    int count = BitConverter.ToUInt16(raw, 4);
                    int bestIdx = 0, bestDiff = int.MaxValue;
                    for (int i = 0; i < count; i++)
                    {
                        int b = 6 + i * 16;
                        int w = raw[b] == 0 ? 256 : raw[b];
                        int d = Math.Abs(w - 32);
                        if (d < bestDiff) { bestDiff = d; bestIdx = i; }
                    }

                    int entryBase = 6 + bestIdx * 16;
                    int dataSize = (int)BitConverter.ToUInt32(raw, entryBase + 8);
                    int dataOff = (int)BitConverter.ToUInt32(raw, entryBase + 12);

                    using (var ms = new MemoryStream(raw, dataOff, dataSize))
                    using (var bmp = new Bitmap(ms))
                        return Icon.FromHandle(bmp.GetHicon());
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
