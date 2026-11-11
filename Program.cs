
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.IO;
using ScottPlot;

class Program
{
    static async Task Main()
    {
        string apiUrl = "https://rc-vault-fap-live-1.azurewebsites.net/api/gettimeentries?code=vO17RnE8vuzXzPJo5eaLLjXjmRW07law99QTD90zat9FfOQJKKUcgQ==";

        try
        {
            Console.WriteLine("Fetching employee time entries...");

            using HttpClient client = new HttpClient();

            HttpResponseMessage response = await client.GetAsync(apiUrl);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    $"API request failed: {(int)response.StatusCode} - {response.ReasonPhrase}");

                string errorResponse = await response.Content.ReadAsStringAsync();

                if (!string.IsNullOrWhiteSpace(errorResponse))
                {
                    Console.WriteLine(errorResponse);
                }

                return;
            }

            string json = await response.Content.ReadAsStringAsync();

            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                Console.WriteLine("Invalid API response. Expected a JSON array.");
                return;
            }

            Dictionary<string, double> employeeHours =
                new Dictionary<string, double>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (JsonElement entry in document.RootElement.EnumerateArray())
            {
                string? name = GetString(entry, "EmployeeName");
                string? start = GetString(entry, "StarTimeUtc");

                start ??= GetString(entry, "StartTimeUtc");

                string? end = GetString(entry, "EndTimeUtc");

                if (string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(start) ||
                    string.IsNullOrWhiteSpace(end))
                {
                    continue;
                }

                // Ignore deleted entries
                if (GetProperty(entry, "DeletedOn", out JsonElement deleted) &&
                    deleted.ValueKind != JsonValueKind.Null)
                {
                    continue;
                }

                // Convert timestamps
                if (!DateTimeOffset.TryParse(
                        start,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal,
                        out DateTimeOffset startTime) ||
                    !DateTimeOffset.TryParse(
                        end,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal,
                        out DateTimeOffset endTime))
                {
                    continue;
                }

                // Calculate working hours
                double hours = (endTime - startTime).TotalHours;

                if (hours <= 0)
                {
                    continue;
                }

                name = name.Trim();

                if (employeeHours.ContainsKey(name))
                {
                    employeeHours[name] += hours;
                }
                else
                {
                    employeeHours[name] = hours;
                }
            }

            // Sort employees by total working hours
            List<EmployeeSummary> employees = employeeHours
                .Select(x => new EmployeeSummary
                {
                    Name = x.Key,
                    Hours = x.Value
                })
                .OrderByDescending(x => x.Hours)
                .ToList();

            if (employees.Count == 0)
            {
                Console.WriteLine("No valid employee records found.");
                return;
            }

            // Create output folder
            Directory.CreateDirectory("Output");

            // Generate HTML report
            GenerateHtml(employees);

            // Generate pie chart
            GeneratePieChart(employees);

            Console.WriteLine();
            Console.WriteLine("Reports generated successfully!");
            Console.WriteLine("HTML Report: Output/EmployeeReport.html");
            Console.WriteLine("Pie Chart: Output/EmployeePieChart.png");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine("API request failed: " + ex.Message);
        }
        catch (JsonException ex)
        {
            Console.WriteLine("Invalid JSON response: " + ex.Message);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
    }

    // Get a JSON string property
    static string? GetString(JsonElement element, string property)
    {
        if (GetProperty(element, property, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    // Find a JSON property without considering uppercase/lowercase
    static bool GetProperty(
        JsonElement element,
        string property,
        out JsonElement value)
    {
        foreach (JsonProperty item in element.EnumerateObject())
        {
            if (string.Equals(
                item.Name,
                property,
                StringComparison.OrdinalIgnoreCase))
            {
                value = item.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    // Generate HTML employee report
    static void GenerateHtml(List<EmployeeSummary> employees)
    {
        StringBuilder html = new StringBuilder();

        html.Append("""
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset="UTF-8">
            <title>Employee Working Hours</title>

            <style>
                body {
                    font-family: Arial, sans-serif;
                    margin: 40px;
                    background: #f4f6f9;
                }

                h1 {
                    text-align: center;
                    color: #263b55;
                }

                table {
                    width: 70%;
                    margin: 30px auto;
                    border-collapse: collapse;
                    background: white;
                }

                th, td {
                    border: 1px solid #ddd;
                    padding: 14px;
                    text-align: center;
                }

                th {
                    background: #263b55;
                    color: white;
                }

                tr.low-hours {
                    background: #ffcccc;
                }

                tr:hover {
                    background: #e8eef5;
                }
            </style>
        </head>

        <body>

            <h1>Employee Working Hours Report</h1>

            <table>
                <tr>
                    <th>Employee Name</th>
                    <th>Total Hours Worked</th>
                </tr>
        """);

        foreach (EmployeeSummary employee in employees)
        {
            string rowClass = employee.Hours < 100
                ? "low-hours"
                : "";

            html.Append($"""
                <tr class="{rowClass}">
                    <td>{WebUtility.HtmlEncode(employee.Name)}</td>
                    <td>{employee.Hours.ToString("F2", CultureInfo.InvariantCulture)}</td>
                </tr>
            """);
        }

        html.Append("""
            </table>

        </body>
        </html>
        """);

        File.WriteAllText(
            "Output/EmployeeReport.html",
            html.ToString(),
            Encoding.UTF8);

        Console.WriteLine("HTML report created successfully.");
    }

    // Generate employee working hours pie chart
    static void GeneratePieChart(List<EmployeeSummary> employees)
    {
        var plot = new ScottPlot.Plot();

        double[] values = employees
            .Select(x => x.Hours)
            .ToArray();

        var pie = plot.Add.Pie(values);

        double totalHours = values.Sum();

        for (int i = 0; i < pie.Slices.Count; i++)
        {
            double percentage =
                (pie.Slices[i].Value / totalHours) * 100;

            pie.Slices[i].Label = $"{percentage:F1}%";

            pie.Slices[i].LegendText =
                $"{employees[i].Name} ({percentage:F1}%)";
        }

        plot.ShowLegend();

        plot.Axes.Frameless();
        plot.HideGrid();

        plot.SavePng(
            "Output/EmployeePieChart.png",
            1000,
            700);

        Console.WriteLine("Pie chart created successfully.");
    }
}

// Employee details
class EmployeeSummary
{
    public string Name { get; set; } = "";

    public double Hours { get; set; }
}
