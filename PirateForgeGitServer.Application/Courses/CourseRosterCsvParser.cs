using System.Text;

namespace PirateForgeGitServer.Application.Courses;

public static class CourseRosterCsvParser
{
    public const int MaximumStudents = 5000;

    public static CourseRosterParseResult Parse(string csv)
    {
        var records = ReadRecords(csv);
        if (records.Count == 0
            || records[0].Fields.Count != 2
            || !string.Equals(
                records[0].Fields[0].TrimStart('\uFEFF').Trim(),
                "email",
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                records[0].Fields[1].Trim(),
                "displayName",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException(
                "The roster must start with the header email,displayName.");
        }

        if (records.Count - 1 > MaximumStudents)
        {
            throw new FormatException(
                $"A roster cannot contain more than {MaximumStudents} students.");
        }

        var students = new List<CourseRosterStudent>();
        var errors = new List<CourseRosterError>();
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records.Skip(1))
        {
            if (record.Fields.Count == 1
                && string.IsNullOrWhiteSpace(record.Fields[0]))
            {
                continue;
            }

            if (record.Fields.Count != 2)
            {
                errors.Add(new(
                    record.RowNumber,
                    "Each row must contain email and displayName."));
                continue;
            }

            var email = record.Fields[0].Trim().ToLowerInvariant();
            var displayName = record.Fields[1].Trim();
            if (string.IsNullOrWhiteSpace(email)
                || string.IsNullOrWhiteSpace(displayName))
            {
                errors.Add(new(
                    record.RowNumber,
                    "Email and display name are required."));
                continue;
            }

            if (!emails.Add(email))
            {
                errors.Add(new(
                    record.RowNumber,
                    "The email is duplicated in this roster."));
                continue;
            }

            students.Add(new(record.RowNumber, email, displayName));
        }

        return new(students, errors);
    }

    private static List<CsvRecord> ReadRecords(string csv)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var rowNumber = 1;
        var recordStart = 1;

        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (character == '"')
            {
                if (inQuotes
                    && index + 1 < csv.Length
                    && csv[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (!inQuotes && character == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
                continue;
            }

            if (!inQuotes && character is '\r' or '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                records.Add(new(recordStart, fields.ToArray()));
                fields.Clear();
                if (character == '\r'
                    && index + 1 < csv.Length
                    && csv[index + 1] == '\n')
                {
                    index++;
                }

                rowNumber++;
                recordStart = rowNumber;
                continue;
            }

            field.Append(character);
            if (character == '\n')
            {
                rowNumber++;
            }
        }

        if (inQuotes)
        {
            throw new FormatException("The roster contains an unclosed quoted field.");
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(new(recordStart, fields.ToArray()));
        }

        return records;
    }

    private sealed record CsvRecord(
        int RowNumber,
        IReadOnlyList<string> Fields);
}

public sealed record CourseRosterParseResult(
    IReadOnlyList<CourseRosterStudent> Students,
    IReadOnlyList<CourseRosterError> Errors);

public sealed record CourseRosterStudent(
    int RowNumber,
    string Email,
    string DisplayName);

public sealed record CourseRosterError(
    int RowNumber,
    string Message);
