using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RvtConvert.App
{
    internal sealed class PlanItem
    {
        public FileEntry File;
        public string Final;          // file .rvt/.rfa đích
        public string Ifc;            // file IFC trung gian (chỉ khi hạ version)
        public RevitInstall Exporter; // Revit dùng để đọc file nguồn khi hạ version
        public bool Failed;
        public bool Done;
        public bool NeedsIfc => Exporter != null;
    }

    /// <summary>
    /// Điều phối cả quá trình:
    ///  - file version thấp/bằng Revit đích  -> mở bằng Revit đích và lưu lại (nâng version, giữ nguyên mô hình);
    ///  - file version cao hơn Revit đích    -> Revit mới (>= version file) xuất IFC, rồi Revit đích nhập IFC và lưu .rvt
    ///    (hạ version: giữ hình khối, mất family/thông số/view).
    /// </summary>
    internal static class Pipeline
    {
        public static async Task<List<string>> RunAsync(IList<PlanItem> plan, RevitInstall target, JobOptions baseOpt,
            Action<string> log, Action<PlanItem, string, string> status, Action onFinalOk, CancellationToken ct)
        {
            var errors = new List<string>();
            string ifcDir = Path.Combine(Path.GetTempPath(), "BIMLabRvtConvert", "ifc_" + Guid.NewGuid().ToString("N"));

            try
            {
                // ---- 1) Nâng version (hoặc lưu lại cùng version) ----
                var up = plan.Where(p => !p.NeedsIfc).ToList();
                if (up.Count > 0)
                {
                    log("== Nâng version bằng Revit " + target.Year + " (" + up.Count + " file) ==");
                    errors.AddRange(await RunStep(up, target, baseOpt, "upgrade", p => p.File.Path, p => p.Final,
                        log, status, onFinalOk, true, ct));
                }

                // ---- 2) Hạ version: xuất IFC ----
                var down = plan.Where(p => p.NeedsIfc).ToList();
                if (down.Count > 0)
                {
                    Directory.CreateDirectory(ifcDir);
                    int n = 0;
                    foreach (var p in down)
                        p.Ifc = Path.Combine(ifcDir, (++n) + "_" + SafeName(Path.GetFileNameWithoutExtension(p.File.Path)) + ".ifc");

                    foreach (var grp in down.GroupBy(p => p.Exporter.Year).OrderBy(g => g.Key))
                    {
                        var ex = grp.First().Exporter;
                        log("== Hạ version (1/2): xuất IFC bằng Revit " + ex.Year + " (" + grp.Count() + " file) ==");
                        errors.AddRange(await RunStep(grp.ToList(), ex, Clone(baseOpt, "ifcexport"), "ifcexport",
                            p => p.File.Path, p => p.Ifc, log, status, null, false, ct));
                    }

                    // ---- 3) Hạ version: nhập IFC vào Revit đích ----
                    var imp = down.Where(p => !p.Failed).ToList();
                    if (imp.Count > 0)
                    {
                        log("== Hạ version (2/2): nhập IFC và lưu .rvt bằng Revit " + target.Year + " (" + imp.Count + " file) ==");
                        errors.AddRange(await RunStep(imp, target, Clone(baseOpt, "ifcimport"), "ifcimport",
                            p => p.Ifc, p => p.Final, log, status, onFinalOk, true, ct));
                    }
                }
            }
            finally
            {
                try { if (Directory.Exists(ifcDir)) Directory.Delete(ifcDir, true); } catch { }
            }
            return errors;
        }

        private static async Task<List<string>> RunStep(List<PlanItem> items, RevitInstall revit, JobOptions opt, string mode,
            Func<PlanItem, string> src, Func<PlanItem, string> dst, Action<string> log,
            Action<PlanItem, string, string> status, Action onFinalOk, bool isFinal, CancellationToken ct)
        {
            opt = Clone(opt, mode);
            opt.Revit = revit;
            var jobs = items.Select((p, i) => new JobItem { Source = src(p), Target = dst(p), Index = i, Tag = p }).ToList();
            foreach (var p in items)
                status(p, mode == "ifcimport" ? "Đang nhập IFC..." : mode == "ifcexport" ? "Đang xuất IFC..." : "Đang chờ Revit...", null);

            var errs = await JobRunner.RunAsync(jobs, opt, log,
                (job, text, kind) =>
                {
                    var p = (PlanItem)job.Tag;
                    if (kind == "ok")
                    {
                        if (isFinal) { p.Done = true; status(p, "Xong - " + Path.GetFileName(p.Final), "ok"); onFinalOk?.Invoke(); }
                        else status(p, "Đã xuất IFC, chờ nhập vào Revit đích...", null);
                    }
                    else if (kind == "err") { p.Failed = true; status(p, "LỖI: " + text.Replace("LỖI: ", ""), "err"); }
                    else status(p, text, null);
                }, ct);

            // file chưa có kết quả (Revit tắt giữa chừng...) -> đánh lỗi
            foreach (var p in items)
                if (!p.Done && !p.Failed && (isFinal || p.Ifc == null || !File.Exists(p.Ifc)))
                {
                    p.Failed = true;
                    status(p, "LỖI: không có kết quả từ Revit", "err");
                }
            return errs;
        }

        private static JobOptions Clone(JobOptions o, string mode) => new JobOptions
        {
            Revit = o.Revit, Detach = o.Detach, Compact = o.Compact, Overwrite = o.Overwrite,
            CloseRevit = o.CloseRevit, Mode = mode,
        };

        private static string SafeName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }
    }
}
