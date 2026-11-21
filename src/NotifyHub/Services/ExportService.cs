using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NotifyHub.Models;

namespace NotifyHub.Services;

public interface IExportService
{
    Task ExportToCsvAsync(IEnumerable<NotificationItem> items, string destinationPath);
    Task ExportToExcelXmlAsync(IEnumerable<NotificationItem> items, string destinationPath);
}

public class ExportService : IExportService
{
    public async Task ExportToCsvAsync(IEnumerable<NotificationItem> items, string destinationPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,NotificationId,Source,Category,Title,Body,ReceivedAt,Status,Priority,Notes,ForwardingStatus");

        foreach (var item in items)
        {
            sb.Append(EscapeCsv(item.DisplayId)).Append(',')
              .Append(EscapeCsv(item.NotificationId)).Append(',')
              .Append(EscapeCsv(item.AppName)).Append(',')
              .Append(EscapeCsv(item.Category)).Append(',')
              .Append(EscapeCsv(item.Title)).Append(',')
              .Append(EscapeCsv(item.Body)).Append(',')
              .Append(EscapeCsv(item.ReceivedAt.ToString("yyyy-MM-dd HH:mm:ss"))).Append(',')
              .Append(EscapeCsv(item.Status.ToString())).Append(',')
              .Append(EscapeCsv(item.Priority.ToString())).Append(',')
              .Append(EscapeCsv(item.UserNotes ?? string.Empty)).Append(',')
              .Append(EscapeCsv(item.ForwardingStatus.ToString()))
              .AppendLine();
        }

        EnsureDirectory(destinationPath);
        await File.WriteAllTextAsync(destinationPath, sb.ToString(), Encoding.UTF8);
    }

    public async Task ExportToExcelXmlAsync(IEnumerable<NotificationItem> items, string destinationPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
        sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">");
        sb.AppendLine("  <Styles>");
        sb.AppendLine("    <Style ss:ID=\"Header\"><Font ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/><Interior ss:Color=\"#0078D4\" ss:Pattern=\"Solid\"/></Style>");
        sb.AppendLine("  </Styles>");
        sb.AppendLine("  <Worksheet ss:Name=\"Notifications\">");
        sb.AppendLine("    <Table>");
        sb.AppendLine("      <Row ss:StyleID=\"Header\">");
        foreach (string header in new[] { "ID", "Source App", "Category", "Title", "Body", "Received", "Status", "Priority", "Notes", "Forwarding" })
        {
            sb.AppendLine($"        <Cell><Data ss:Type=\"String\">{header}</Data></Cell>");
        }
        sb.AppendLine("      </Row>");

        foreach (var item in items)
        {
            sb.AppendLine("      <Row>");
            sb.AppendLine($"        <Cell><Data ss:Type=\"String\">{item.DisplayId}</Data></Cell>");
            sb.AppendLine($"        <Cell><Data ss:Type=\"String\">{EscapeXml(item.AppName)}</Data></Cell>");
            sb.AppendLine($"        <Cell><Data ss:Type=\"String\">{EscapeXml(item.Category)}</Data></Cell>");
            sb.AppendLine($"        <Cell><Data ss:Type=\"String\">{EscapeXml(item.Title)}</Data></Cell>");
            sb.AppendLine($"        <Cell><Data ss:Type=\"String\">{EscapeXml(item.Body)}</Data></Cell>");
            sb.AppendLine($"        <Cell><Data ss:Type=\"String\">{item.ReceivedAt:yyyy-MM-dd HH:mm:ss}</Data></Cell>");
            sb.AppendLine($"        <Cell><Data ss:Type=\"String\">{item.Status}</Data></Cell>");
            sb.AppendLine($"        <Cell><Data ss:Type=\"String\">{item.Priority}</Data></Cell>");
            sb.AppendLine($"        <Cell><Data ss:Type=\"String\">{EscapeXml(item.UserNotes ?? string.Empty)}</Data></Cell>");
            sb.AppendLine($"        <Cell><Data ss:Type=\"String\">{item.ForwardingStatus}</Data></Cell>");
            sb.AppendLine("      </Row>");
        }

        sb.AppendLine("    </Table>");
        sb.AppendLine("  </Worksheet>");
        sb.AppendLine("</Workbook>");

        EnsureDirectory(destinationPath);
        await File.WriteAllTextAsync(destinationPath, sb.ToString(), Encoding.UTF8);
    }

    private static void EnsureDirectory(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";

        // Prevent CSV Formula Injection in spreadsheet apps
        string safe = value;
        if (safe.Length > 0 && (safe[0] == '=' || safe[0] == '+' || safe[0] == '-' || safe[0] == '@' || safe[0] == '\t' || safe[0] == '\r'))
        {
            safe = "'" + safe;
        }

        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }

    private static string EscapeXml(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
