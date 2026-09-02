using System.Text;
using Nova.Features.Players;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Shouldly;

namespace Nova.Unit.Tests.Features.Players;

/// <summary>Exercises the strict, bounded player CSV parser.</summary>
public sealed class PlayerImportCsvParserTests
{
    private const string Header = "First name,Last name,Date of birth,Gender,Jersey number,Graduation year\r\n";
    private readonly PlayerImportCsvParser _parser = new();

    [Fact]
    public void Parse_ReturnsTypedReadyRows_ForStrictUtf8Csv()
    {
        var result = Parse("Zoë,李,2012-02-29,female,7,2030\r\n", includeBom: true);

        result.IsT0.ShouldBeTrue();
        var row = result.AsT0.Rows.ShouldHaveSingleItem();
        row.SourceRowNumber.ShouldBe(2);
        row.Status.ShouldBe(PlayerImportRowStatus.Ready);
        row.Values.FirstName.ShouldBe("Zoë");
        row.Values.LastName.ShouldBe("李");
        row.Candidate.ShouldNotBeNull();
        row.Candidate.DateOfBirth.ShouldBe(new DateOnly(2012, 2, 29));
        row.Candidate.Gender.ShouldBe(Gender.Female);
        row.Candidate.JerseyNumber.ShouldBe(7);
    }

    [Fact]
    public void Parse_PreservesLogicalSourceRows_AcrossBlankAndMultilineRecords()
    {
        var result = Parse("Alex,Archer,2012-01-01,,1,2030\r\n\r\n\"Mary\nAnn\",Smith,2011-03-04,,,2029\r\n");

        result.IsT0.ShouldBeTrue();
        result.AsT0.Rows.Select(row => row.SourceRowNumber).ShouldBe([2, 3, 4]);
        result.AsT0.Rows[1].Status.ShouldBe(PlayerImportRowStatus.Invalid);
        result.AsT0.Rows[2].Values.FirstName.ShouldBe("Mary\nAnn");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("=cmd")]
    [InlineData(" +cmd")]
    [InlineData("-cmd")]
    [InlineData("@cmd")]
    [InlineData("\tcmd")]
    [InlineData(" \t=cmd")]
    public void Parse_RejectsFormulaLikeCells(string firstName)
    {
        var result = Parse($"{firstName},Archer,2012-01-01,,,2030\r\n");

        result.IsT0.ShouldBeTrue();
        var row = result.AsT0.Rows.ShouldHaveSingleItem();
        row.Status.ShouldBe(PlayerImportRowStatus.Invalid);
        row.Errors.ShouldContain(error =>
            error.Field == PlayerImportField.FirstName
            && error.Message.Contains("formula", StringComparison.Ordinal));
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("01/02/2012", "", "12", "2030", PlayerImportField.DateOfBirth)]
    [InlineData("2012-01-02", "unknown", "12", "2030", PlayerImportField.Gender)]
    [InlineData("2012-01-02", "0", "12", "2030", PlayerImportField.Gender)]
    [InlineData("2012-01-02", "", "+12", "2030", PlayerImportField.JerseyNumber)]
    [InlineData("2012-01-02", "", "12", "\"2,030\"", PlayerImportField.GraduationYear)]
    public void Parse_RejectsLocaleOrNonContractValues(
        string dateOfBirth,
        string gender,
        string jerseyNumber,
        string graduationYear,
        PlayerImportField expectedField)
    {
        var result = Parse($"Alex,Archer,{dateOfBirth},{gender},{jerseyNumber},{graduationYear}\r\n");

        result.IsT0.ShouldBeTrue();
        result.AsT0.Rows.ShouldHaveSingleItem().Errors.ShouldContain(error => error.Field == expectedField);
    }

    [Fact]
    public void Parse_ReusesCreatePlayerValidation_ForNamesAndRanges()
    {
        var longName = new string('a', 101);
        var result = Parse($"{longName}, ,2012-01-01,,10000,1999\r\n");

        var errors = result.AsT0.Rows.ShouldHaveSingleItem().Errors;
        errors.Select(error => error.Field).ShouldContain(PlayerImportField.FirstName);
        errors.Select(error => error.Field).ShouldContain(PlayerImportField.LastName);
        errors.Select(error => error.Field).ShouldContain(PlayerImportField.JerseyNumber);
        errors.Select(error => error.Field).ShouldContain(PlayerImportField.GraduationYear);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("first name,Last name,Date of birth,Gender,Jersey number,Graduation year\r\n")]
    [InlineData("Last name,First name,Date of birth,Gender,Jersey number,Graduation year\r\n")]
    [InlineData("First name,Last name,Date of birth,Gender,Jersey number\r\n")]
    [InlineData("First name,Last name,Date of birth,Gender,Jersey number,Graduation year,Extra\r\n")]
    public void Parse_RejectsAnyHeaderDrift(string header)
    {
        var result = _parser.Parse(
            Encoding.UTF8.GetBytes(header + "Alex,Archer,2012-01-01,,,2030\r\n"),
            TestContext.Current.CancellationToken);

        result.IsT1.ShouldBeTrue();
        result.AsT1.Message.ShouldContain("header row");
    }

    [Fact]
    public void Parse_RejectsHeaderOnlyFile()
    {
        var result = _parser.Parse(Encoding.UTF8.GetBytes(Header), TestContext.Current.CancellationToken);

        result.IsT1.ShouldBeTrue();
        result.AsT1.Message.ShouldContain("at least one data row");
    }

    [Fact]
    public void Parse_RejectsInvalidUtf8()
    {
        var bytes = Encoding.UTF8.GetBytes(Header + "Alex,");
        bytes = [.. bytes, 0xC3, 0x28];

        var result = _parser.Parse(bytes, TestContext.Current.CancellationToken);

        result.IsT1.ShouldBeTrue();
        result.AsT1.Message.ShouldContain("UTF-8");
    }

    [Fact]
    public void Parse_RejectsUtf16Preamble()
    {
        var result = _parser.Parse(
            [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Header + "Alex,Archer,2012-01-01,,,2030\r\n")],
            TestContext.Current.CancellationToken);

        result.IsT1.ShouldBeTrue();
        result.AsT1.Message.ShouldContain("UTF-8");
    }

    [Fact]
    public void Parse_RejectsInconsistentColumnCount()
    {
        var result = Parse("Alex,Archer,2012-01-01,2030\r\n");

        result.IsT1.ShouldBeTrue();
        result.AsT1.Message.ShouldContain("exactly 6 columns");
    }

    [Fact]
    public void Parse_RejectsAllEmptyWrongWidthRecord()
    {
        var result = Parse(",,,,,,\r\n");

        result.IsT1.ShouldBeTrue();
        result.AsT1.Message.ShouldContain("exactly 6 columns");
    }

    [Fact]
    public void Parse_AcceptsMaximumRows_AndRejectsOneMore()
    {
        var thousandRows = string.Concat(Enumerable.Repeat("Alex,Archer,2012-01-01,,,2030\r\n", 1_000));

        Parse(thousandRows).AsT0.Rows.Count.ShouldBe(1_000);
        var excessive = Parse(thousandRows + "Taylor,Archer,2013-01-01,,,2031\r\n");
        excessive.IsT1.ShouldBeTrue();
        excessive.AsT1.Message.ShouldContain("no more than 1000");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0, "First name")]
    [InlineData(5, "Graduation year")]
    public void Parse_RejectsOversizedField_WithSourceRowAndField(int fieldIndex, string fieldName)
    {
        var cells = new[] { "Alex", "Archer", "2012-01-01", "", "", "2030" };
        cells[fieldIndex] = new string('a', PlayerImportConstraints.MaxFieldCharacters + 1);
        var result = Parse(string.Join(',', cells) + "\r\n");

        result.IsT1.ShouldBeTrue();
        result.AsT1.Message.ShouldContain("Source row 2");
        result.AsT1.Message.ShouldContain($"field '{fieldName}'");
        result.AsT1.Message.ShouldContain($"{PlayerImportConstraints.MaxFieldCharacters} characters");
    }

    [Fact]
    public void Parse_RejectsMalformedQuoting()
    {
        var result = Parse("\"Alex,Archer,2012-01-01,,,2030\r\n");

        result.IsT1.ShouldBeTrue();
        result.AsT1.Message.ShouldContain("malformed CSV");
    }

    [Fact]
    public void Parse_ObservesCancellationBetweenRecords()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Should.Throw<OperationCanceledException>(() =>
            _parser.Parse(Encoding.UTF8.GetBytes(Header + "Alex,Archer,2012-01-01,,,2030\r\n"), cancellation.Token));
    }

    private OneOf.OneOf<ParsedPlayerImport, PlayerImportFileFailure> Parse(
        string rows,
        bool includeBom = false)
    {
        var body = Encoding.UTF8.GetBytes(Header + rows);
        var content = includeBom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body;
        return _parser.Parse(content, TestContext.Current.CancellationToken);
    }
}
