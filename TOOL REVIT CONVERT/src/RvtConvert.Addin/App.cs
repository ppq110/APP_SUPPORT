using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RvtConvert.Addin
{
    /// <summary>
    /// Add-in "chạy ngầm" cho tool BIMLab Revit Convert. Chỉ hoạt động khi Revit được tool khởi động
    /// với biến môi trường BIMLAB_RVTCONVERT_JOB trỏ tới file job; Revit chạy bình thường thì không làm gì.
    ///
    /// File job (text): mỗi dòng "khóa=giá trị" hoặc "item&lt;TAB&gt;nguồn&lt;TAB&gt;đích".
    /// Kết quả ghi nối tiếp vào file result: START/OK/ERR/DONE (tool đọc để hiện tiến độ).
    /// </summary>
    public class App : IExternalApplication
    {
        private const string JobEnv = "BIMLAB_RVTCONVERT_JOB";

        private string _resultFile;
        private string _logFile;
        private bool _detach = true, _compact, _overwrite = true, _closeRevit = true;
        private readonly List<string[]> _items = new List<string[]>();
        private string _mode = "upgrade"; // upgrade | ifcexport | ifcimport
        private bool _ran;

        public Result OnStartup(UIControlledApplication app)
        {
            string job = Environment.GetEnvironmentVariable(JobEnv);
            if (string.IsNullOrEmpty(job) || !File.Exists(job))
                return Result.Succeeded;

            try
            {
                ReadJob(job);
                app.Idling += OnIdling;
                app.DialogBoxShowing += OnDialog;
                app.ControlledApplication.FailuresProcessing += OnFailures;
            }
            catch (Exception ex)
            {
                Log("OnStartup lỗi: " + ex);
            }
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

        private void ReadJob(string path)
        {
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                if (line.StartsWith("item\t"))
                {
                    var p = line.Split('\t');
                    if (p.Length >= 3) _items.Add(new[] { p[1], p[2] });
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                switch (k)
                {
                    case "mode": _mode = v; break;
                    case "result": _resultFile = v; break;
                    case "log": _logFile = v; break;
                    case "detach": _detach = v == "1"; break;
                    case "compact": _compact = v == "1"; break;
                    case "overwrite": _overwrite = v == "1"; break;
                    case "closeRevit": _closeRevit = v == "1"; break;
                }
            }
        }

        private void OnIdling(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            if (_ran) return;
            _ran = true;
            try
            {
                var uiApp = sender as UIApplication;
                ((UIApplication)sender).Idling -= OnIdling;
                Report("READY");
                RunJob(uiApp.Application);
            }
            catch (Exception ex)
            {
                Log("Job lỗi: " + ex);
                Report("FATAL\t" + OneLine(ex.Message));
            }
            finally
            {
                Report("DONE");
                if (_closeRevit) CloseRevit();
            }
        }

        private void RunJob(Autodesk.Revit.ApplicationServices.Application app)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                string src = _items[i][0], dst = _items[i][1];
                Report("START\t" + i + "\t" + src);
                var sw = Stopwatch.StartNew();
                Document doc = null;
                try
                {
                    if (_mode != "ifcexport" && !_overwrite && File.Exists(dst))
                        throw new InvalidOperationException("File đích đã tồn tại.");

                    doc = _mode == "ifcimport" ? OpenIfc(app, src) : Open(app, src);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));

                    if (_mode == "ifcexport")
                    {
                        ExportIfc(doc, dst);
                    }
                    else
                    {
                        var so = new SaveAsOptions { OverwriteExistingFile = true, Compact = _compact };
                        if (doc.IsWorkshared)
                            so.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });
                        doc.SaveAs(dst, so);
                    }
                    Report("OK\t" + i + "\t" + dst + "\t" + (sw.ElapsedMilliseconds / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                }
                catch (Exception ex)
                {
                    Log("Lỗi file " + src + ": " + ex);
                    Report("ERR\t" + i + "\t" + OneLine(ex.Message));
                }
                finally
                {
                    try { doc?.Close(false); } catch (Exception ex) { Log("Close lỗi: " + ex.Message); }
                }
            }
        }

        private Document Open(Autodesk.Revit.ApplicationServices.Application app, string src)
        {
            ModelPath mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(src);

            var opt = new OpenOptions();
            opt.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
            if (_detach)
                opt.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;

            try
            {
                return app.OpenDocumentFile(mp, opt);
            }
            catch (Exception ex) when (_detach)
            {
                // Một số file (không workshare) có thể không nhận tùy chọn detach -> thử lại không detach.
                Log("Mở với detach lỗi (" + ex.Message + ") -> thử lại không detach.");
                var plain = new OpenOptions();
                plain.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
                return app.OpenDocumentFile(mp, plain);
            }
        }

        private Document OpenIfc(Autodesk.Revit.ApplicationServices.Application app, string ifcPath)
        {
            return app.OpenIFCDocument(ifcPath);
        }

        private void ExportIfc(Document doc, string dst)
        {
            string dir = Path.GetDirectoryName(dst);
            string name = Path.GetFileNameWithoutExtension(dst);
            Exception last = null;
            foreach (IFCVersion ver in new[] { IFCVersion.IFC4, IFCVersion.IFC2x3 })
            {
                try
                {
                    bool ok;
                    using (var t = new Transaction(doc, "BIMLab IFC export"))
                    {
                        t.Start();
                        var o = new IFCExportOptions { FileVersion = ver, ExportBaseQuantities = true };
                        ok = doc.Export(dir, name, o);
                        t.RollBack();
                    }
                    if (ok && File.Exists(dst)) { Log("Xuất IFC (" + ver + ") thành công: " + dst); return; }
                    last = new InvalidOperationException("Revit không tạo được file IFC (" + ver + ").");
                }
                catch (Exception ex)
                {
                    last = ex;
                    Log("Xuất IFC " + ver + " lỗi: " + ex.Message);
                }
            }
            throw last ?? new InvalidOperationException("Xuất IFC thất bại.");
        }

        private void OnDialog(object sender, DialogBoxShowingEventArgs e)
        {
            try
            {
                Log("Dialog: " + e.DialogId);
                if (e is TaskDialogShowingEventArgs)
                    e.OverrideResult((int)TaskDialogResult.Close);
                else
                    e.OverrideResult(1); // IDOK
            }
            catch (Exception ex) { Log("OnDialog lỗi: " + ex.Message); }
        }

        private void OnFailures(object sender, Autodesk.Revit.DB.Events.FailuresProcessingEventArgs e)
        {
            try
            {
                FailuresAccessor fa = e.GetFailuresAccessor();
                fa.DeleteAllWarnings();
                e.SetProcessingResult(FailureProcessingResult.Continue);
            }
            catch (Exception ex) { Log("OnFailures lỗi: " + ex.Message); }
        }

        // ---- tiện ích ----
        private static string OneLine(string s) =>
            (s ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");

        private void Report(string line) => Append(_resultFile, line);

        private void Log(string line) => Append(_logFile, DateTime.Now.ToString("HH:mm:ss ") + line);

        private static void Append(string file, string line)
        {
            if (string.IsNullOrEmpty(file)) return;
            for (int i = 0; i < 5; i++)
            {
                try { File.AppendAllText(file, line + Environment.NewLine); return; }
                catch (IOException) { System.Threading.Thread.Sleep(50); }
                catch { return; }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private void CloseRevit()
        {
            try { PostMessage(Process.GetCurrentProcess().MainWindowHandle, 0x0010 /*WM_CLOSE*/, IntPtr.Zero, IntPtr.Zero); }
            catch (Exception ex) { Log("Đóng Revit lỗi: " + ex.Message); }
        }
    }
}
