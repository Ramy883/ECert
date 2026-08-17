using ECert.Data;
using ECert.Models;
using ECert.Services;
using ECert.ViewModels;
using ClosedXML.Excel;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECert.Controllers;

[Authorize]
public class CoursesManageController : Controller
{
    private readonly ECertDbContext _db;
    private readonly AuditLogService _audit;
    private readonly IWebHostEnvironment _env;
    public CoursesManageController(ECertDbContext db, AuditLogService audit, IWebHostEnvironment env)
    { _db = db; _audit = audit; _env = env; }

    private bool HasPermission(string perm) => User.HasClaim(c => c.Type == "Permission" && c.Value == perm);

    private async Task LoadCourseFormOptions()
    {
        ViewBag.Categories = await _db.Categories.Where(c => c.IsActive).ToListAsync();
        ViewBag.Instructors = await _db.Instructors.Where(i => i.IsActive).ToListAsync();
        ViewBag.CertificateDesigns = await _db.CertificateDesigns
            .OrderByDescending(d => d.IsPublished)
            .ThenBy(d => d.Name)
            .ToListAsync();
    }

    private async Task<string?> SaveImage(IFormFile? image)
    {
        if (image == null || image.Length == 0) return null;
        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "courses");
        Directory.CreateDirectory(uploadsDir);
        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(image.FileName)}";
        var filePath = Path.Combine(uploadsDir, fileName);
        using var stream = new FileStream(filePath, FileMode.Create);
        await image.CopyToAsync(stream);
        return $"/uploads/courses/{fileName}";
    }

    public async Task<IActionResult> Index(string? search)
    {
        if (!HasPermission("manage-courses")) return Forbid();
        var query = _db.Courses.Include(c => c.Category).Include(c => c.Instructor).Include(c => c.Registrations).AsQueryable();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(c => (c.CourseNameArabic != null && c.CourseNameArabic.Contains(search)) || (c.CourseNameEnglish != null && c.CourseNameEnglish.Contains(search)) || c.CourseName.Contains(search));
        ViewBag.Search = search;
        var courses = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();
        return View(courses);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!HasPermission("manage-courses")) return Forbid();
        await LoadCourseFormOptions();
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Import()
    {
        if (!HasPermission("manage-courses")) return Forbid();
        return View(new CourseImportPageViewModel());
    }

    [HttpGet]
    public IActionResult DownloadImportTemplate()
    {
        if (!HasPermission("manage-courses")) return Forbid();
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Courses");
        var headers = new[]
        {
            "CourseName", "Category", "Instructor", "Price", "DiscountType", "DiscountValue",
            "TotalSeats", "BookedSeats", "StartDate", "EndDate", "Location", "Status",
            "IsFeatured", "RequiresAcademicDetails", "ShortDescription", "FullDescription", "Objectives", "Content"
        };
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#2563eb");
        sheet.Row(1).Style.Font.FontColor = XLColor.White;
        sheet.Cell(2, 1).Value = "مثال: القيادة الإدارية الفعالة";
        sheet.Cell(2, 2).Value = "اسم الفئة كما يظهر في النظام";
        sheet.Cell(2, 3).Value = "اسم المدرب كما يظهر في النظام";
        sheet.Cell(2, 4).Value = 1200;
        sheet.Cell(2, 5).Value = "Percentage";
        sheet.Cell(2, 6).Value = 10;
        sheet.Cell(2, 7).Value = 20;
        sheet.Cell(2, 8).Value = 0;
        sheet.Cell(2, 9).Value = DateTime.Today.AddDays(7);
        sheet.Cell(2, 10).Value = DateTime.Today.AddDays(14);
        sheet.Cell(2, 12).Value = "Draft";
        sheet.Cell(2, 13).Value = false;
        sheet.Cell(2, 14).Value = false;
        sheet.Row(2).Style.Font.FontColor = XLColor.Gray;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
        sheet.Column(1).Width = 34;
        sheet.Column(15).Width = 34;
        sheet.Column(16).Width = 46;
        sheet.Column(17).Width = 40;
        sheet.Column(18).Width = 40;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "courses-import-template.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Import(IFormFile? file)
    {
        if (!HasPermission("manage-courses")) return Forbid();
        var model = new CourseImportPageViewModel();
        if (file == null || file.Length == 0)
        {
            ModelState.AddModelError("File", "اختر ملف XLSX صالحاً.");
            return View(model);
        }
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase) || file.Length > 10_000_000)
        {
            ModelState.AddModelError("File", "يسمح فقط بملفات XLSX بحجم لا يتجاوز 10 ميجابايت.");
            return View(model);
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var preview = await BuildImportPreview(stream);
            var token = Guid.NewGuid().ToString("N");
            preview.Token = token;
            preview.CreatedAtUtc = DateTime.UtcNow;
            var previewDir = Path.Combine(Path.GetTempPath(), "ecert-course-imports");
            Directory.CreateDirectory(previewDir);
            await System.IO.File.WriteAllTextAsync(Path.Combine(previewDir, token + ".json"), JsonSerializer.Serialize(preview));
            model.PreviewToken = token;
            model.Preview = preview;
            return View(model);
        }
        catch (Exception)
        {
            ModelState.AddModelError("File", "تعذر قراءة الملف. تأكد أنه ملف XLSX سليم وغير محمي بكلمة مرور.");
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmImport(string token)
    {
        if (!HasPermission("manage-courses")) return Forbid();
        if (string.IsNullOrWhiteSpace(token) || token.Length != 32) return BadRequest("رمز المعاينة غير صالح.");
        var previewPath = Path.Combine(Path.GetTempPath(), "ecert-course-imports", token + ".json");
        if (!System.IO.File.Exists(previewPath))
        {
            TempData["Error"] = "انتهت صلاحية المعاينة. ارفع الملف مرة أخرى.";
            return RedirectToAction(nameof(Import));
        }
        CourseImportPreview? preview;
        try
        {
            preview = JsonSerializer.Deserialize<CourseImportPreview>(await System.IO.File.ReadAllTextAsync(previewPath));
        }
        finally
        {
            try { System.IO.File.Delete(previewPath); } catch { }
        }
        if (preview == null || preview.CreatedAtUtc < DateTime.UtcNow.AddHours(-1) || preview.InvalidRows > 0 || preview.ValidRows == 0)
        {
            TempData["Error"] = "لا يمكن اعتماد الملف. يجب معالجة جميع الأخطاء أولاً.";
            return RedirectToAction(nameof(Import));
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var names = preview.Rows.Select(r => Normalize(r.CourseName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingNames = (await _db.Courses.Select(c => c.CourseName).ToListAsync())
                .Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (names.Any(existingNames.Contains))
            {
                TempData["Error"] = "توجد دورات بالاسم نفسه في النظام. لم يتم حفظ أي صف لتجنب التكرار.";
                return RedirectToAction(nameof(Import));
            }
            foreach (var row in preview.Rows)
            {
                _db.Courses.Add(new Course
                {
                    CourseName = row.CourseName,
                    CategoryId = row.CategoryId!.Value,
                    InstructorId = row.InstructorId!.Value,
                    Price = row.Price,
                    DiscountType = row.DiscountType,
                    DiscountValue = row.DiscountValue,
                    TotalSeats = row.TotalSeats,
                    BookedSeats = row.BookedSeats,
                    StartDate = row.StartDate,
                    EndDate = row.EndDate,
                    Location = row.Location,
                    Status = row.Status,
                    IsFeatured = row.IsFeatured,
                    RequiresAcademicDetails = row.RequiresAcademicDetails,
                    ShortDescription = row.ShortDescription,
                    FullDescription = row.FullDescription,
                    Objectives = row.Objectives,
                    Content = row.Content,
                    CreatedAt = DateTime.Now
                });
            }
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            await _audit.LogAsync(User.Identity?.Name ?? "", "BulkCreate", "Course", null, null, $"Imported {preview.ValidRows} courses");
            TempData["Success"] = $"تم استيراد {preview.ValidRows} دورة بنجاح دون أخطاء جزئية.";
        }
        catch
        {
            await transaction.RollbackAsync();
            TempData["Error"] = "فشل حفظ الاستيراد بالكامل، ولم يتم حفظ أي دورة.";
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task<CourseImportPreview> BuildImportPreview(Stream stream)
    {
        var preview = new CourseImportPreview();
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault() ?? throw new InvalidDataException();
        var used = sheet.RangeUsed() ?? throw new InvalidDataException();
        if (used.RowCount() < 2 || used.ColumnCount() < 4) throw new InvalidDataException();
        if (used.RowCount() > 5001) throw new InvalidDataException("The workbook exceeds the 5000-row limit.");
        var headers = Enumerable.Range(1, used.ColumnCount()).ToDictionary(i => CanonicalHeader(sheet.Cell(1, i).GetString()), i => i);
        var required = new[] { "coursename", "category", "instructor", "price", "totalseats" };
        var missing = required.Where(h => !headers.ContainsKey(h)).ToList();
        if (missing.Count > 0) throw new InvalidDataException($"Missing columns: {string.Join(",", missing)}");

        var categories = await _db.Categories.Where(c => c.IsActive).ToDictionaryAsync(c => Normalize(c.CategoryName), c => c.CategoryId);
        var instructors = await _db.Instructors.Where(i => i.IsActive).ToDictionaryAsync(i => Normalize(i.FullName), i => i.InstructorId);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var rowNumber = 2; rowNumber <= used.LastRow().RowNumber(); rowNumber++)
        {
            var row = new CourseImportRow { RowNumber = rowNumber };
            row.CourseName = Text(sheet, headers, rowNumber, "coursename");
            row.Category = Text(sheet, headers, rowNumber, "category");
            row.Instructor = Text(sheet, headers, rowNumber, "instructor");
            row.ShortDescription = OptionalText(sheet, headers, rowNumber, "shortdescription");
            row.FullDescription = OptionalText(sheet, headers, rowNumber, "fulldescription");
            row.Objectives = OptionalText(sheet, headers, rowNumber, "objectives");
            row.Content = OptionalText(sheet, headers, rowNumber, "content");
            row.Location = OptionalText(sheet, headers, rowNumber, "location");
            row.Status = OptionalText(sheet, headers, rowNumber, "status") ?? "Draft";
            row.DiscountType = OptionalText(sheet, headers, rowNumber, "discounttype");
            row.IsFeatured = ParseBool(sheet, headers, rowNumber, "isfeatured");
            row.RequiresAcademicDetails = ParseBool(sheet, headers, rowNumber, "requiresacademicdetails");
            row.Price = ParseDecimal(sheet, headers, rowNumber, "price", row.Errors);
            row.DiscountValue = ParseDecimal(sheet, headers, rowNumber, "discountvalue", row.Errors);
            row.TotalSeats = ParseInt(sheet, headers, rowNumber, "totalseats", row.Errors);
            row.BookedSeats = ParseInt(sheet, headers, rowNumber, "bookedseats", row.Errors);
            row.StartDate = ParseDate(sheet, headers, rowNumber, "startdate", row.Errors);
            row.EndDate = ParseDate(sheet, headers, rowNumber, "enddate", row.Errors);
            if (string.IsNullOrWhiteSpace(row.CourseName)) row.Errors.Add("اسم الدورة مطلوب");
            if (row.CourseName.Length > 200) row.Errors.Add("اسم الدورة يتجاوز 200 حرف");
            if (string.IsNullOrWhiteSpace(Text(sheet, headers, rowNumber, "price"))) row.Errors.Add("السعر مطلوب");
            if (!categories.TryGetValue(Normalize(row.Category), out var categoryId)) row.Errors.Add("الفئة غير موجودة أو غير نشطة"); else row.CategoryId = categoryId;
            if (!instructors.TryGetValue(Normalize(row.Instructor), out var instructorId)) row.Errors.Add("المدرب غير موجود أو غير نشط"); else row.InstructorId = instructorId;
            if (!seen.Add(Normalize(row.CourseName))) row.Errors.Add("اسم الدورة مكرر داخل الملف");
            if (row.Price < 0) row.Errors.Add("السعر لا يمكن أن يكون سالباً");
            if (row.DiscountValue < 0) row.Errors.Add("الخصم لا يمكن أن يكون سالباً");
            if (row.DiscountType is not (null or "" or "Percentage" or "Fixed")) row.Errors.Add("نوع الخصم يجب أن يكون Percentage أو Fixed");
            if (row.DiscountType == "Percentage" && row.DiscountValue > 100) row.Errors.Add("الخصم النسبي لا يمكن أن يتجاوز 100%");
            if (row.TotalSeats <= 0) row.Errors.Add("عدد المقاعد يجب أن يكون أكبر من صفر");
            if (row.BookedSeats < 0 || row.BookedSeats > row.TotalSeats) row.Errors.Add("المقاعد المحجوزة خارج النطاق");
            if (row.StartDate.HasValue && row.EndDate.HasValue && row.EndDate < row.StartDate) row.Errors.Add("تاريخ النهاية قبل تاريخ البداية");
            var statuses = new[] { "Draft", "Published", "OpenForRegistration", "Full", "InProgress", "Completed", "Archived" };
            if (!statuses.Contains(row.Status, StringComparer.OrdinalIgnoreCase)) row.Errors.Add("حالة الدورة غير معروفة");
            preview.Rows.Add(row);
        }
        preview.TotalRows = preview.Rows.Count;
        preview.ValidRows = preview.Rows.Count(r => r.IsValid);
        preview.InvalidRows = preview.Rows.Count - preview.ValidRows;
        return preview;
    }

    private static string Normalize(string value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static string NormalizeHeader(string value) => Normalize(value).Replace(" ", "").Replace("_", "").ToLowerInvariant();
    private static string CanonicalHeader(string value)
    {
        var header = NormalizeHeader(value);
        return header switch
        {
            "اسم الدورة" or "اسم الدوره" or "الدورة" or "الدوره" => "coursename",
            "الفئة" or "الفئه" => "category",
            "المدرب" or "اسم المدرب" => "instructor",
            "السعر" or "سعر الدورة" or "سعر الدوره" => "price",
            "نوع الخصم" => "discounttype",
            "قيمة الخصم" => "discountvalue",
            "عدد المقاعد" => "totalseats",
            "المقاعد المحجوزة" or "المقاعد المحجوزه" => "bookedseats",
            "تاريخ البداية" or "تاريخ البدء" => "startdate",
            "تاريخ النهاية" or "تاريخ الانتهاء" => "enddate",
            "المكان" => "location",
            "الحالة" => "status",
            "مميزة" or "دورة مميزة" or "دوره مميزه" => "isfeatured",
            "تتطلب بيانات أكاديمية" or "تتطلب بيانات اكاديمية" => "requiresacademicdetails",
            "الوصف المختصر" => "shortdescription",
            "الوصف الكامل" => "fulldescription",
            "أهداف الدورة" or "اهداف الدورة" or "أهداف الدوره" or "اهداف الدوره" => "objectives",
            "محتوى الدورة" or "محتوى الدوره" => "content",
            _ => header
        };
    }
    private static string Text(IXLWorksheet sheet, Dictionary<string, int> headers, int row, string key) => headers.TryGetValue(key, out var col) ? sheet.Cell(row, col).GetString().Trim() : string.Empty;
    private static string? OptionalText(IXLWorksheet sheet, Dictionary<string, int> headers, int row, string key) => string.IsNullOrWhiteSpace(Text(sheet, headers, row, key)) ? null : Text(sheet, headers, row, key);
    private static decimal ParseDecimal(IXLWorksheet sheet, Dictionary<string, int> headers, int row, string key, List<string> errors)
    {
        var value = Text(sheet, headers, row, key);
        if (string.IsNullOrWhiteSpace(value)) return 0;
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result) || decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result)) return result;
        errors.Add($"القيمة الرقمية في {key} غير صالحة"); return 0;
    }
    private static int ParseInt(IXLWorksheet sheet, Dictionary<string, int> headers, int row, string key, List<string> errors)
    {
        var value = Text(sheet, headers, row, key);
        if (string.IsNullOrWhiteSpace(value)) return 0;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) || int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out result)) return result;
        errors.Add($"القيمة الصحيحة في {key} غير صالحة"); return 0;
    }
    private static DateTime? ParseDate(IXLWorksheet sheet, Dictionary<string, int> headers, int row, string key, List<string> errors)
    {
        if (!headers.TryGetValue(key, out var column)) return null;
        var value = Text(sheet, headers, row, key);
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (sheet.Cell(row, column).TryGetValue<DateTime>(out var date)) return date.Date;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out date)) return date.Date;
        errors.Add($"التاريخ في {key} غير صالح"); return null;
    }
    private static bool ParseBool(IXLWorksheet sheet, Dictionary<string, int> headers, int row, string key)
    {
        var value = OptionalText(sheet, headers, row, key);
        return value is not null && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("نعم") || value == "1");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Course course, IFormFile? image)
    {
        if (!HasPermission("manage-courses")) return Forbid();

        course.CourseNameEnglish = course.CourseNameEnglish?.Trim() ?? string.Empty;
        course.CourseNameArabic = course.CourseNameArabic?.Trim() ?? string.Empty;
        course.CourseName = course.CourseNameArabic;

        if (!ModelState.IsValid)
        {
            ViewBag.Categories = await _db.Categories.Where(c => c.IsActive).ToListAsync();
            ViewBag.Instructors = await _db.Instructors.Where(i => i.IsActive).ToListAsync();
            return View(course);
        }

        course.CreatedAt = DateTime.Now;
        var imgPath = await SaveImage(image);
        if (imgPath != null) course.ImageUrl = imgPath;
        _db.Courses.Add(course);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Create", "Course", course.CourseId, null, $"{course.CourseNameEnglish} / {course.CourseNameArabic}");
        TempData["Success"] = "تمت إضافة الدورة بنجاح.";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        if (!HasPermission("manage-courses")) return Forbid();
        var course = await _db.Courses.FindAsync(id);
        if (course == null) return NotFound();
        await LoadCourseFormOptions();
        return View(course);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Course course, IFormFile? image)
    {
        if (!HasPermission("manage-courses")) return Forbid();

        course.CourseNameEnglish = course.CourseNameEnglish?.Trim() ?? string.Empty;
        course.CourseNameArabic = course.CourseNameArabic?.Trim() ?? string.Empty;
        course.CourseName = course.CourseNameArabic;

        if (!ModelState.IsValid)
        {
            ViewBag.Categories = await _db.Categories.Where(c => c.IsActive).ToListAsync();
            ViewBag.Instructors = await _db.Instructors.Where(i => i.IsActive).ToListAsync();
            return View(course);
        }

        var existing = await _db.Courses.FindAsync(course.CourseId);
        if (existing == null) return NotFound();

        existing.CourseName = course.CourseNameArabic;
        existing.CourseNameEnglish = course.CourseNameEnglish;
        existing.CourseNameArabic = course.CourseNameArabic;
        existing.ShortDescription = course.ShortDescription;
        existing.FullDescription = course.FullDescription;
        existing.Objectives = course.Objectives;
        existing.Content = course.Content;
        existing.CategoryId = course.CategoryId;
        existing.InstructorId = course.InstructorId;
        existing.CertificateDesignId = course.CertificateDesignId;
        existing.StartDate = course.StartDate;
        existing.EndDate = course.EndDate;
        existing.Location = course.Location;
        existing.Price = course.Price;
        existing.DiscountType = course.DiscountType;
        existing.DiscountValue = course.DiscountValue;
        existing.TotalSeats = course.TotalSeats;
        existing.Status = course.Status;
        existing.IsFeatured = course.IsFeatured;
        existing.RequiresAcademicDetails = course.RequiresAcademicDetails;
        var imgPath = await SaveImage(image);
        if (imgPath != null)
        {
            // Delete old image if exists
            if (!string.IsNullOrEmpty(existing.ImageUrl))
            {
                var oldImgPath = Path.Combine(_env.WebRootPath, existing.ImageUrl.TrimStart('/'));
                if (System.IO.File.Exists(oldImgPath))
                    System.IO.File.Delete(oldImgPath);
            }
            existing.ImageUrl = imgPath;
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Update", "Course", course.CourseId, null, $"{course.CourseNameEnglish} / {course.CourseNameArabic}");
        TempData["Success"] = "تم تعديل الدورة بنجاح.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(int id, string status)
    {
        if (!HasPermission("manage-courses")) return Forbid();
        var course = await _db.Courses.FindAsync(id);
        if (course == null) return NotFound();
        course.Status = status;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Update", "Course", id, null, $"Status: {status}");
        TempData["Success"] = "تم تغيير حالة الدورة.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (!HasPermission("manage-courses")) return Forbid();
        var course = await _db.Courses.FindAsync(id);
        if (course == null) return NotFound();

        // Delete image file from disk
        if (!string.IsNullOrEmpty(course.ImageUrl))
        {
            var imgPath = Path.Combine(_env.WebRootPath, course.ImageUrl.TrimStart('/'));
            if (System.IO.File.Exists(imgPath))
                System.IO.File.Delete(imgPath);
        }

        course.Status = "Archived";
        course.ImageUrl = null;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(User.Identity?.Name ?? "", "Delete", "Course", id);
        TempData["Success"] = "تم أرشفة الدورة.";
        return RedirectToAction("Index");
    }
}
