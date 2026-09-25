using System.Xml.Linq;

namespace DuckDB.OlapExtension.Tests;

public class XmlaParserTests
{
    // Wraps a <row> fragment into a minimal XMLA rowset envelope with the given
    // column definitions.
    private static XDocument Wrap(string schemaElements, string rowsFragment)
    {
        var xml = $"""
            <root xmlns="urn:schemas-microsoft-com:xml-analysis:rowset"
                  xmlns:xsd="http://www.w3.org/2001/XMLSchema"
                  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  xmlns:sql="urn:schemas-microsoft-com:xml-sql">
              <xsd:schema>
                <xsd:complexType name="row">
                  <xsd:sequence>
                    {schemaElements}
                  </xsd:sequence>
                </xsd:complexType>
              </xsd:schema>
              {rowsFragment}
            </root>
            """;
        return XDocument.Parse(xml);
    }

    private static string Col(string name, string type = "xsd:int", string? field = null)
    {
        var fieldAttr = field is null ? "" : $""" sql:field="{field}" """.Trim();
        return $"""<xsd:element name="{name}" type="{type}"{fieldAttr}/>""";
    }

    // ---------------------------------------------------------------------
    // #1: NULL must not shift columns
    // ---------------------------------------------------------------------

    [Fact]
    public void NullInMiddle_DoesNotShiftColumns()
    {
        var doc = Wrap(
            Col("A") + Col("B") + Col("C"),
            "<row><A>1</A><C>3</C></row>");

        var result = XmlaParser.ParseRowset(doc);

        var row = result.Rows[0];
        Assert.Equal(1, row[0]);
        Assert.Null(row[1]);      // B was absent → NULL
        Assert.Equal(3, row[2]);  // C stays in position 3
    }

    [Fact]
    public void NullAtStart_DoesNotShiftColumns()
    {
        var doc = Wrap(
            Col("A") + Col("B") + Col("C"),
            "<row><B>2</B><C>3</C></row>");

        var row = XmlaParser.ParseRowset(doc).Rows[0];

        Assert.Null(row[0]);
        Assert.Equal(2, row[1]);
        Assert.Equal(3, row[2]);
    }

    [Fact]
    public void NullAtEnd_DoesNotShiftColumns()
    {
        var doc = Wrap(
            Col("A") + Col("B") + Col("C"),
            "<row><A>1</A><B>2</B></row>");

        var row = XmlaParser.ParseRowset(doc).Rows[0];

        Assert.Equal(1, row[0]);
        Assert.Equal(2, row[1]);
        Assert.Null(row[2]);
    }

    // ---------------------------------------------------------------------
    // #2: Empty string vs NULL
    // ---------------------------------------------------------------------

    [Fact]
    public void EmptyString_StaysEmptyString_NotNull()
    {
        var doc = Wrap(
            Col("A", "xsd:string"),
            "<row><A/></row>");

        var row = XmlaParser.ParseRowset(doc).Rows[0];

        Assert.Equal("", row[0]);
    }

    [Fact]
    public void XsiNilTrue_BecomesNull_ForString()
    {
        var doc = Wrap(
            Col("A", "xsd:string"),
            """<row><A xsi:nil="true"/></row>""");

        var row = XmlaParser.ParseRowset(doc).Rows[0];

        Assert.Null(row[0]);
    }

    [Fact]
    public void EmptyString_ForNumericType_BecomesNull()
    {
        var doc = Wrap(
            Col("A"),
            "<row><A/></row>");

        var row = XmlaParser.ParseRowset(doc).Rows[0];

        Assert.Null(row[0]);
    }

    // ---------------------------------------------------------------------
    // #3: DateTime handling
    // ---------------------------------------------------------------------

    [Fact]
    public void DateTimeWithoutOffset_IsUnspecifiedKind()
    {
        var doc = Wrap(
            Col("A", "xsd:dateTime"),
            "<row><A>2024-01-15T10:30:00</A></row>");

        var row = XmlaParser.ParseRowset(doc).Rows[0];
        var dt = Assert.IsType<DateTime>(row[0]);

        Assert.Equal(DateTimeKind.Unspecified, dt.Kind);
        Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 0), dt);
    }

    [Fact]
    public void DateTimeWithZ_IsUtc()
    {
        var doc = Wrap(
            Col("A", "xsd:dateTime"),
            "<row><A>2024-01-15T10:30:00Z</A></row>");

        var row = XmlaParser.ParseRowset(doc).Rows[0];
        var dt = Assert.IsType<DateTime>(row[0]);

        Assert.Equal(DateTimeKind.Utc, dt.Kind);
        Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc), dt);
    }

    [Fact]
    public void DateTimeWithOffset_IsConvertedToUtc()
    {
        var doc = Wrap(
            Col("A", "xsd:dateTime"),
            "<row><A>2024-01-15T10:30:00+05:00</A></row>");

        var row = XmlaParser.ParseRowset(doc).Rows[0];
        var dt = Assert.IsType<DateTime>(row[0]);

        Assert.Equal(DateTimeKind.Utc, dt.Kind);
        Assert.Equal(new DateTime(2024, 1, 15, 5, 30, 0, DateTimeKind.Utc), dt);
    }

    // ---------------------------------------------------------------------
    // #4: Type mismatch / overflow must throw
    // ---------------------------------------------------------------------

    [Fact]
    public void IntegerOverflow_Throws()
    {
        var doc = Wrap(
            Col("A", "xsd:int"),
            "<row><A>2147483648</A></row>");   // Int32.MaxValue + 1

        var ex = Assert.Throws<InvalidOperationException>(
            () => XmlaParser.ParseRowset(doc));

        Assert.Contains("A", ex.Message);
        Assert.Contains("Int32", ex.Message);
    }

    // ---------------------------------------------------------------------
    // Other behaviours
    // ---------------------------------------------------------------------

    [Fact]
    public void EmptyResult_NoRows()
    {
        var doc = Wrap(Col("A"), "");

        var result = XmlaParser.ParseRowset(doc);

        Assert.Single(result.Columns);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void UnknownElement_IsIgnored()
    {
        var doc = Wrap(
            Col("A"),
            "<row><A>1</A><Z>999</Z></row>");

        var row = XmlaParser.ParseRowset(doc).Rows[0];

        Assert.Single(row);
        Assert.Equal(1, row[0]);
    }

    [Fact]
    public void MultipleRows_AllParsed()
    {
        var doc = Wrap(
            Col("A"),
            "<row><A>1</A></row><row><A>2</A></row><row><A>3</A></row>");

        var result = XmlaParser.ParseRowset(doc);

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(1, result.Rows[0][0]);
        Assert.Equal(2, result.Rows[1][0]);
        Assert.Equal(3, result.Rows[2][0]);
    }
}