using PirateForgeGitServer.Application.Courses;

namespace PirateForgeGitServer.UnitTests.Courses;

public sealed class CourseRosterCsvParserTests
{
    [Fact]
    public void ParseSupportsQuotedDisplayNames()
    {
        const string csv =
            """
            email,displayName
            anne@example.edu,"Bonny, Anne"
            """;

        var result = CourseRosterCsvParser.Parse(csv);

        var student = Assert.Single(result.Students);
        Assert.Equal(2, student.RowNumber);
        Assert.Equal("anne@example.edu", student.Email);
        Assert.Equal("Bonny, Anne", student.DisplayName);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ParseReportsMalformedAndDuplicateRowsWithoutDiscardingValidRows()
    {
        const string csv =
            """
            email,displayName
            valid@example.edu,Valid Student
            missing-name@example.edu,
            too,many,columns
            VALID@example.edu,Duplicate Student
            """;

        var result = CourseRosterCsvParser.Parse(csv);

        Assert.Single(result.Students);
        Assert.Equal(3, result.Errors.Count);
        Assert.Contains(result.Errors, error => error.RowNumber == 3);
        Assert.Contains(result.Errors, error => error.RowNumber == 4);
        Assert.Contains(result.Errors, error => error.RowNumber == 5);
    }

    [Fact]
    public void ParseRequiresTheDocumentedHeader()
    {
        const string csv = "name,email\nStudent,student@example.edu";

        var exception = Assert.Throws<FormatException>(() =>
            CourseRosterCsvParser.Parse(csv));

        Assert.Contains("email,displayName", exception.Message);
    }
}
