using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using CodeBrix.PostgresClient.PgSqlTypes;
using SilverAssertions;
using Xunit;

namespace CodeBrix.PostgresClient.Tests; //was previously: Npgsql.Tests;

public class PgSqlParameterTest : TestBase, IClassFixture<PgSqlParameterTestFixture>
{
    // Makes sure that when PgSqlDbType or Value/PgSqlValue are set, DbType and PgSqlDbType are set accordingly
    [Fact]
    public void implicit_setting_of_DbType()
    {
        var p = new PgSqlParameter("p", DbType.Int32);
        p.PgSqlDbType.Should().Be(PgSqlDbType.Integer);

        // As long as PgSqlDbType/DbType aren't set explicitly, infer them from Value
        p = new PgSqlParameter("p", 8);
        p.PgSqlDbType.Should().Be(PgSqlDbType.Integer);
        p.DbType.Should().Be(DbType.Int32);

        p.Value = 3.0;
        p.PgSqlDbType.Should().Be(PgSqlDbType.Double);
        p.DbType.Should().Be(DbType.Double);

        p.PgSqlDbType = PgSqlDbType.Bytea;
        p.PgSqlDbType.Should().Be(PgSqlDbType.Bytea);
        p.DbType.Should().Be(DbType.Binary);

        p.Value = "dont_change";
        p.PgSqlDbType.Should().Be(PgSqlDbType.Bytea);
        p.DbType.Should().Be(DbType.Binary);

        p = new PgSqlParameter("p", new int[0]);
        p.PgSqlDbType.Should().Be(PgSqlDbType.Array | PgSqlDbType.Integer);
        p.DbType.Should().Be(DbType.Object);
    }

    [Fact]
    public void DataTypeName()
    {
        using var conn = OpenConnection();
        using var cmd = new PgSqlCommand("SELECT @p", conn);
        var p1 = new PgSqlParameter { ParameterName = "p", Value = 8, DataTypeName = "integer" };
        cmd.Parameters.Add(p1);
        cmd.ExecuteScalar().Should().Be(8);
        // Purposefully try to send int as string, which should fail. This makes sure
        // the above doesn't work simply because of type inference from the CLR type.
        p1.DataTypeName = "text";
        Assert.Throws<InvalidCastException>(() => cmd.ExecuteScalar());

        cmd.Parameters.Clear();

        var p2 = new PgSqlParameter<int> { ParameterName = "p", TypedValue = 8, DataTypeName = "integer" };
        cmd.Parameters.Add(p2);
        cmd.ExecuteScalar().Should().Be(8);
        // Purposefully try to send int as string, which should fail. This makes sure
        // the above doesn't work simply because of type inference from the CLR type.
        p2.DataTypeName = "text";
        Assert.Throws<InvalidCastException>(() => cmd.ExecuteScalar());
    }

    [Fact]
    public void positional_parameter_is_positional()
    {
        var p = new PgSqlParameter(PgSqlParameter.PositionalName, 1);
        p.IsPositional.Should().BeTrue();

        var p2 = new PgSqlParameter(null, 1);
        p2.IsPositional.Should().BeTrue();
    }

    [Fact]
    public void infer_data_type_name_from_PgSqlDbType()
    {
        //Arrange
        var p = new PgSqlParameter("par_field1", PgSqlDbType.Varchar, 50);

        //Assert
        p.DataTypeName.Should().Be("character varying");
    }

    [Fact]
    public void infer_data_type_name_from_DbType()
    {
        //Arrange
        var p = new PgSqlParameter("par_field1", DbType.String , 50);

        //Assert
        p.DataTypeName.Should().Be("text");
    }

    [Fact]
    public void infer_data_type_name_from_PgSqlDbType_for_array()
    {
        //Arrange
        var p = new PgSqlParameter("int_array", PgSqlDbType.Array | PgSqlDbType.Integer);

        //Assert
        p.DataTypeName.Should().Be("integer[]");
    }

    [Fact]
    public void infer_data_type_name_from_PgSqlDbType_for_built_in_range()
    {
        //Arrange
        var p = new PgSqlParameter("numeric_range", PgSqlDbType.Range | PgSqlDbType.Numeric);

        //Assert
        p.DataTypeName.Should().Be("numrange");
    }

    [Fact]
    public void cannot_infer_data_type_name_from_PgSqlDbType_for_unknown_range()
    {
        //Arrange
        var p = new PgSqlParameter("text_range", PgSqlDbType.Range | PgSqlDbType.Text);

        //Assert
        p.DataTypeName.Should().Be(null);
    }

    [Fact]
    public void infer_data_type_name_from_ClrType()
    {
        //Arrange
        var p = new PgSqlParameter("p1", Array.Empty<byte>());

        //Assert
        p.DataTypeName.Should().Be("bytea");
    }

    [Fact]
    public void setting_DbType_sets_PgSqlDbType()
    {
        //Arrange
        var p = new PgSqlParameter();
        p.DbType = DbType.Binary;

        //Assert
        p.PgSqlDbType.Should().Be(PgSqlDbType.Bytea);
    }

    [Fact]
    public void setting_PgSqlDbType_sets_DbType()
    {
        //Arrange
        var p = new PgSqlParameter();
        p.PgSqlDbType = PgSqlDbType.Bytea;

        //Assert
        p.DbType.Should().Be(DbType.Binary);
    }

    [Fact]
    public void setting_value_does_not_change_DbType()
    {
        //Arrange
        var p = new PgSqlParameter { DbType = DbType.Binary, PgSqlDbType = PgSqlDbType.Bytea };
        p.Value = 8;

        //Assert
        p.DbType.Should().Be(DbType.Binary);
        p.PgSqlDbType.Should().Be(PgSqlDbType.Bytea);
    }

    // Older tests

    #region Constructors

    [Fact]
    public void constructor1()
    {
        //Arrange
        var p = new PgSqlParameter();

        //Assert
        p.DbType.Should().Be(DbType.Object, "DbType");
        p.Direction.Should().Be(ParameterDirection.Input, "Direction");
        p.IsNullable.Should().BeFalse("IsNullable");
        p.ParameterName.Should().BeEmpty("ParameterName");
        p.Precision.Should().Be(0, "Precision");
        p.Scale.Should().Be(0, "Scale");
        p.Size.Should().Be(0, "Size");
        p.SourceColumn.Should().BeEmpty("SourceColumn");
        p.SourceVersion.Should().Be(DataRowVersion.Current, "SourceVersion");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Unknown, "PgSqlDbType");
        p.Value.Should().BeNull("Value");
    }

    [Fact]
    public void constructor2_Value_DateTime()
    {
        var value = new DateTime(2004, 8, 24);

        var p = new PgSqlParameter("address", value);
        p.DbType.Should().Be(DbType.DateTime2, "B:DbType");
        p.Direction.Should().Be(ParameterDirection.Input, "B:Direction");
        p.IsNullable.Should().BeFalse("B:IsNullable");
        p.ParameterName.Should().Be("address", "B:ParameterName");
        p.Precision.Should().Be(0, "B:Precision");
        p.Scale.Should().Be(0, "B:Scale");
        //Assert.AreEqual (0, p.Size, "B:Size");
        p.SourceColumn.Should().BeEmpty("B:SourceColumn");
        p.SourceVersion.Should().Be(DataRowVersion.Current, "B:SourceVersion");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Timestamp, "B:PgSqlDbType");
        p.Value.Should().Be(value, "B:Value");
    }

    [Fact]
    public void constructor2_Value_DBNull()
    {
        //Arrange
        var p = new PgSqlParameter("address", DBNull.Value);

        //Assert
        p.DbType.Should().Be(DbType.Object, "B:DbType");
        p.Direction.Should().Be(ParameterDirection.Input, "B:Direction");
        p.IsNullable.Should().BeFalse("B:IsNullable");
        p.ParameterName.Should().Be("address", "B:ParameterName");
        p.Precision.Should().Be(0, "B:Precision");
        p.Scale.Should().Be(0, "B:Scale");
        p.Size.Should().Be(0, "B:Size");
        p.SourceColumn.Should().BeEmpty("B:SourceColumn");
        p.SourceVersion.Should().Be(DataRowVersion.Current, "B:SourceVersion");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Unknown, "B:PgSqlDbType");
        p.Value.Should().Be(DBNull.Value, "B:Value");
    }

    [Fact]
    public void constructor2_Value_null()
    {
        //Arrange
        var p = new PgSqlParameter("address", null);

        //Assert
        p.DbType.Should().Be(DbType.Object, "A:DbType");
        p.Direction.Should().Be(ParameterDirection.Input, "A:Direction");
        p.IsNullable.Should().BeFalse("A:IsNullable");
        p.ParameterName.Should().Be("address", "A:ParameterName");
        p.Precision.Should().Be(0, "A:Precision");
        p.Scale.Should().Be(0, "A:Scale");
        p.Size.Should().Be(0, "A:Size");
        p.SourceColumn.Should().BeEmpty("A:SourceColumn");
        p.SourceVersion.Should().Be(DataRowVersion.Current, "A:SourceVersion");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Unknown, "A:PgSqlDbType");
        p.Value.Should().BeNull("A:Value");
    }

    [Fact]
    //.ctor (String, PgSqlDbType, Int32, String, ParameterDirection, bool, byte, byte, DataRowVersion, object)
    public void constructor7()
    {
        var p1 = new PgSqlParameter("p1Name", PgSqlDbType.Varchar, 20,
            "srcCol", ParameterDirection.InputOutput, false, 0, 0,
            DataRowVersion.Original, "foo");
        p1.DbType.Should().Be(DbType.String, "DbType");
        p1.Direction.Should().Be(ParameterDirection.InputOutput, "Direction");
        p1.IsNullable.Should().Be(false, "IsNullable");
        //Assert.AreEqual (999, p1.LocaleId, "#");
        p1.ParameterName.Should().Be("p1Name", "ParameterName");
        p1.Precision.Should().Be(0, "Precision");
        p1.Scale.Should().Be(0, "Scale");
        p1.Size.Should().Be(20, "Size");
        p1.SourceColumn.Should().Be("srcCol", "SourceColumn");
        p1.SourceColumnNullMapping.Should().Be(false, "SourceColumnNullMapping");
        p1.SourceVersion.Should().Be(DataRowVersion.Original, "SourceVersion");
        p1.PgSqlDbType.Should().Be(PgSqlDbType.Varchar, "PgSqlDbType");
        //Assert.AreEqual (3210, p1.PgSqlValue, "#");
        p1.Value.Should().Be("foo", "Value");
        //Assert.AreEqual ("database", p1.XmlSchemaCollectionDatabase, "XmlSchemaCollectionDatabase");
        //Assert.AreEqual ("name", p1.XmlSchemaCollectionName, "XmlSchemaCollectionName");
        //Assert.AreEqual ("schema", p1.XmlSchemaCollectionOwningSchema, "XmlSchemaCollectionOwningSchema");
    }

    [Fact]
    public void Clone()
    {
        //Arrange
        var expected = new PgSqlParameter
        {
            Value = 42,
            ParameterName = "TheAnswer",

            DbType = DbType.Int32,
            PgSqlDbType = PgSqlDbType.Integer,
            DataTypeName = "integer",

            Direction = ParameterDirection.InputOutput,
            IsNullable = true,
            Precision = 1,
            Scale = 2,
            Size = 4,

            SourceVersion = DataRowVersion.Proposed,
            SourceColumn = "source",
            SourceColumnNullMapping = true,
        };
        var actual = expected.Clone();

        //Assert
        actual.Value.Should().Be(expected.Value);
        actual.ParameterName.Should().Be(expected.ParameterName);

        actual.DbType.Should().Be(expected.DbType);
        actual.PgSqlDbType.Should().Be(expected.PgSqlDbType);
        actual.DataTypeName.Should().Be(expected.DataTypeName);

        actual.Direction.Should().Be(expected.Direction);
        actual.IsNullable.Should().Be(expected.IsNullable);
        actual.Precision.Should().Be(expected.Precision);
        actual.Scale.Should().Be(expected.Scale);
        actual.Size.Should().Be(expected.Size);

        actual.SourceVersion.Should().Be(expected.SourceVersion);
        actual.SourceColumn.Should().Be(expected.SourceColumn);
        actual.SourceColumnNullMapping.Should().Be(expected.SourceColumnNullMapping);
    }

    [Fact]
    public void Clone_generic()
    {
        //Arrange
        var expected = new PgSqlParameter<int>
        {
            TypedValue = 42,
            ParameterName = "TheAnswer",

            DbType = DbType.Int32,
            PgSqlDbType = PgSqlDbType.Integer,
            DataTypeName = "integer",

            Direction = ParameterDirection.InputOutput,
            IsNullable = true,
            Precision = 1,
            Scale = 2,
            Size = 4,

            SourceVersion = DataRowVersion.Proposed,
            SourceColumn ="source",
            SourceColumnNullMapping = true,
        };
        var actual = (PgSqlParameter<int>)expected.Clone();

        //Assert
        actual.Value.Should().Be(expected.Value);
        actual.TypedValue.Should().Be(expected.TypedValue);
        actual.ParameterName.Should().Be(expected.ParameterName);

        actual.DbType.Should().Be(expected.DbType);
        actual.PgSqlDbType.Should().Be(expected.PgSqlDbType);
        actual.DataTypeName.Should().Be(expected.DataTypeName);

        actual.Direction.Should().Be(expected.Direction);
        actual.IsNullable.Should().Be(expected.IsNullable);
        actual.Precision.Should().Be(expected.Precision);
        actual.Scale.Should().Be(expected.Scale);
        actual.Size.Should().Be(expected.Size);

        actual.SourceVersion.Should().Be(expected.SourceVersion);
        actual.SourceColumn.Should().Be(expected.SourceColumn);
        actual.SourceColumnNullMapping.Should().Be(expected.SourceColumnNullMapping);
    }

    #endregion

    [Fact] // bug #320196
    public void parameter_null()
    {
        var param = new PgSqlParameter("param", PgSqlDbType.Numeric);
        param.Scale.Should().Be(0, "#A1");
        param.Value = DBNull.Value;
        param.Scale.Should().Be(0, "#A2");

        param = new PgSqlParameter("param", PgSqlDbType.Integer);
        param.Scale.Should().Be(0, "#B1");
        param.Value = DBNull.Value;
        param.Scale.Should().Be(0, "#B2");
    }

    [Fact]
    public void parameter_type()
    {
        PgSqlParameter p;

        // If Type is not set, then type is inferred from the value
        // assigned. The Type should be inferred everytime Value is assigned
        // If value is null or DBNull, then the current Type should be reset to Unknown (DbType.Object and PgSqlDbType.Unknown).
        p = new PgSqlParameter { Value = "" };
        p.DbType.Should().Be(DbType.String, "#A1");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Text, "#A2");
        p.Value = DBNull.Value;
        p.DbType.Should().Be(DbType.Object, "#B1");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Unknown, "#B2");
        p.Value = 1;
        p.DbType.Should().Be(DbType.Int32, "#C1");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Integer, "#C2");
        p.Value = DBNull.Value;
        p.DbType.Should().Be(DbType.Object, "#D1");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Unknown, "#D2");
        p.Value = new byte[] { 0x0a };
        p.DbType.Should().Be(DbType.Binary, "#E1");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Bytea, "#E2");
        p.Value = null;
        p.DbType.Should().Be(DbType.Object, "#F1");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Unknown, "#F2");
        p.Value = DateTime.Now;
        p.DbType.Should().Be(DbType.DateTime2, "#G1");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Timestamp, "#G2");
        p.Value = null;
        p.DbType.Should().Be(DbType.Object, "#H1");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Unknown, "#H2");

        // If DbType is set, then the PgSqlDbType should not be
        // inferred from the value assigned.
        p = new PgSqlParameter();
        p.DbType = DbType.DateTime;
        p.PgSqlDbType.Should().Be(PgSqlDbType.TimestampTz, "#I1");
        p.Value = 1;
        p.PgSqlDbType.Should().Be(PgSqlDbType.TimestampTz, "#I2");
        p.Value = null;
        p.PgSqlDbType.Should().Be(PgSqlDbType.TimestampTz, "#I3");
        p.Value = DBNull.Value;
        p.PgSqlDbType.Should().Be(PgSqlDbType.TimestampTz, "#I4");

        // If PgSqlDbType is set, then the DbType should not be
        // inferred from the value assigned.
        p = new PgSqlParameter();
        p.PgSqlDbType = PgSqlDbType.Bytea;
        p.PgSqlDbType.Should().Be(PgSqlDbType.Bytea, "#J1");
        p.Value = 1;
        p.PgSqlDbType.Should().Be(PgSqlDbType.Bytea, "#J2");
        p.Value = null;
        p.PgSqlDbType.Should().Be(PgSqlDbType.Bytea, "#J3");
        p.Value = DBNull.Value;
        p.PgSqlDbType.Should().Be(PgSqlDbType.Bytea, "#J4");
    }

    [Fact]
    [IssueLink("https://github.com/npgsql/npgsql/issues/5428")]
    public async Task match_param_index_case_insensitively()
    {
        //Arrange
        await using var conn = await OpenConnectionAsync();
        await using var cmd = new PgSqlCommand("SELECT @p,@P", conn);
        cmd.Parameters.AddWithValue("p", "Hello world");

        //Act
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void ParameterName()
    {
        var p = new PgSqlParameter();
        p.ParameterName = "name";
        p.ParameterName.Should().Be("name", "#A:ParameterName");
        p.SourceColumn.Should().BeEmpty("#A:SourceColumn");

        p.ParameterName = null;
        p.ParameterName.Should().BeEmpty("#B:ParameterName");
        p.SourceColumn.Should().BeEmpty("#B:SourceColumn");

        p.ParameterName = " ";
        p.ParameterName.Should().Be(" ", "#C:ParameterName");
        p.SourceColumn.Should().BeEmpty("#C:SourceColumn");

        p.ParameterName = " name ";
        p.ParameterName.Should().Be(" name ", "#D:ParameterName");
        p.SourceColumn.Should().BeEmpty("#D:SourceColumn");

        p.ParameterName = string.Empty;
        p.ParameterName.Should().BeEmpty("#E:ParameterName");
        p.SourceColumn.Should().BeEmpty("#E:SourceColumn");
    }

    [Fact]
    public void ResetDbType()
    {
        PgSqlParameter p;

        //Parameter with an assigned value but no DbType specified
        p = new PgSqlParameter("foo", 42);
        p.ResetDbType();
        p.DbType.Should().Be(DbType.Int32, "#A:DbType");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Integer, "#A:PgSqlDbType");
        p.Value.Should().Be(42, "#A:Value");

        p.DbType = DbType.DateTime; //assigning a DbType
        p.DbType.Should().Be(DbType.DateTime, "#B:DbType1");
        p.PgSqlDbType.Should().Be(PgSqlDbType.TimestampTz, "#B:SqlDbType1");
        p.ResetDbType();
        p.DbType.Should().Be(DbType.Int32, "#B:DbType2");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Integer, "#B:SqlDbtype2");

        //Parameter with an assigned PgSqlDbType but no specified value
        p = new PgSqlParameter("foo", PgSqlDbType.Integer);
        p.ResetDbType();
        p.DbType.Should().Be(DbType.Object, "#C:DbType");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Unknown, "#C:PgSqlDbType");

        p.PgSqlDbType = PgSqlDbType.TimestampTz; //assigning a PgSqlDbType
        p.DbType.Should().Be(DbType.DateTime, "#D:DbType1");
        p.PgSqlDbType.Should().Be(PgSqlDbType.TimestampTz, "#D:SqlDbType1");
        p.ResetDbType();
        p.DbType.Should().Be(DbType.Object, "#D:DbType2");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Unknown, "#D:SqlDbType2");

        p = new PgSqlParameter();
        p.Value = DateTime.MaxValue;
        p.DbType.Should().Be(DbType.DateTime2, "#E:DbType1");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Timestamp, "#E:SqlDbType1");
        p.Value = null;
        p.ResetDbType();
        p.DbType.Should().Be(DbType.Object, "#E:DbType2");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Unknown, "#E:SqlDbType2");

        p = new PgSqlParameter("foo", PgSqlDbType.Varchar);
        p.Value = DateTime.MaxValue;
        p.ResetDbType();
        p.DbType.Should().Be(DbType.DateTime2, "#F:DbType");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Timestamp, "#F:PgSqlDbType");
        p.Value.Should().Be(DateTime.MaxValue, "#F:Value");

        p = new PgSqlParameter("foo", PgSqlDbType.Varchar);
        p.Value = DBNull.Value;
        p.ResetDbType();
        p.DbType.Should().Be(DbType.Object, "#G:DbType");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Unknown, "#G:PgSqlDbType");
        p.Value.Should().Be(DBNull.Value, "#G:Value");

        p = new PgSqlParameter("foo", PgSqlDbType.Varchar);
        p.Value = null;
        p.ResetDbType();
        p.DbType.Should().Be(DbType.Object, "#G:DbType");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Unknown, "#G:PgSqlDbType");
        p.Value.Should().BeNull("#G:Value");
    }

    [Fact]
    public void ParameterName_retains_prefix()
        => new PgSqlParameter("@p", DbType.String).ParameterName.Should().Be("@p");

    [Fact]
    public void SourceColumn()
    {
        var p = new PgSqlParameter();
        p.SourceColumn = "name";
        p.ParameterName.Should().BeEmpty("#A:ParameterName");
        p.SourceColumn.Should().Be("name", "#A:SourceColumn");

        p.SourceColumn = null;
        p.ParameterName.Should().BeEmpty("#B:ParameterName");
        p.SourceColumn.Should().BeEmpty("#B:SourceColumn");

        p.SourceColumn = " ";
        p.ParameterName.Should().BeEmpty("#C:ParameterName");
        p.SourceColumn.Should().Be(" ", "#C:SourceColumn");

        p.SourceColumn = " name ";
        p.ParameterName.Should().BeEmpty("#D:ParameterName");
        p.SourceColumn.Should().Be(" name ", "#D:SourceColumn");

        p.SourceColumn = string.Empty;
        p.ParameterName.Should().BeEmpty("#E:ParameterName");
        p.SourceColumn.Should().BeEmpty("#E:SourceColumn");
    }

    [Fact]
    public void bug_1011100_PgSqlDbType()
    {
        var p = new PgSqlParameter();
        p.Value = DBNull.Value;
        p.DbType.Should().Be(DbType.Object, "#A:DbType");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Unknown, "#A:PgSqlDbType");

        // Now change parameter value.
        // Note that as we didn't explicitly specified a dbtype, the dbtype property should change when
        // the value changes...

        p.Value = 8;

        p.DbType.Should().Be(DbType.Int32, "#A:DbType");
        p.PgSqlDbType.Should().Be(PgSqlDbType.Integer, "#A:PgSqlDbType");

        //Assert.AreEqual(3510, p.Value, "#A:Value");
        //p.PgSqlDbType = PgSqlDbType.Varchar;
        //Assert.AreEqual(DbType.String, p.DbType, "#B:DbType");
        //Assert.AreEqual(PgSqlDbType.Varchar, p.PgSqlDbType, "#B:PgSqlDbType");
        //Assert.AreEqual(3510, p.Value, "#B:Value");
    }

    [Fact]
    public void PgSqlParameter_Clone()
    {
        //Arrange
        var param = new PgSqlParameter();

        param.Value = 5;
        param.Precision = 1;
        param.Scale = 1;
        param.Size = 1;
        param.Direction = ParameterDirection.Input;
        param.IsNullable = true;
        param.ParameterName = "parameterName";
        param.SourceColumn = "source_column";
        param.SourceVersion = DataRowVersion.Current;
        param.PgSqlValue = 5;
        param.SourceColumnNullMapping = false;

        var newParam = param.Clone();

        //Assert
        newParam.Value.Should().Be(param.Value);
        newParam.Precision.Should().Be(param.Precision);
        newParam.Scale.Should().Be(param.Scale);
        newParam.Size.Should().Be(param.Size);
        newParam.Direction.Should().Be(param.Direction);
        newParam.IsNullable.Should().Be(param.IsNullable);
        newParam.ParameterName.Should().Be(param.ParameterName);
        newParam.TrimmedName.Should().Be(param.TrimmedName);
        newParam.SourceColumn.Should().Be(param.SourceColumn);
        newParam.SourceVersion.Should().Be(param.SourceVersion);
        newParam.PgSqlValue.Should().Be(param.PgSqlValue);
        newParam.SourceColumnNullMapping.Should().Be(param.SourceColumnNullMapping);
        newParam.PgSqlValue.Should().Be(param.PgSqlValue);

    }

    [Fact]
    public void Precision_via_interface()
    {
        //Arrange
        var parameter = new PgSqlParameter();
        var paramIface = (IDbDataParameter)parameter;

        paramIface.Precision = 42;

        //Assert
        paramIface.Precision.Should().Be((byte)42);
    }

    [Fact]
    public void Precision_via_base_class()
    {
        //Arrange
        var parameter = new PgSqlParameter();
        var paramBase = (DbParameter)parameter;

        paramBase.Precision = 42;

        //Assert
        paramBase.Precision.Should().Be((byte)42);
    }

    [Fact]
    public void Scale_via_interface()
    {
        //Arrange
        var parameter = new PgSqlParameter();
        var paramIface = (IDbDataParameter)parameter;

        paramIface.Scale = 42;

        //Assert
        paramIface.Scale.Should().Be((byte)42);
    }

    [Fact]
    public void Scale_via_base_class()
    {
        //Arrange
        var parameter = new PgSqlParameter();
        var paramBase = (DbParameter)parameter;

        paramBase.Scale = 42;

        //Assert
        paramBase.Scale.Should().Be((byte)42);
    }

    [Fact]
    public void null_value_throws()
    {
        //Arrange
        using var connection = OpenConnection();
        using var command = new PgSqlCommand("SELECT @p", connection)
        {
            Parameters = { new PgSqlParameter("p", null) }
        };

        //Assert
        Assert.Throws<InvalidOperationException>(() => command.ExecuteReader());
    }

    [Fact]
    public void null_value_with_nullable_type()
    {
        //Arrange
        using var connection = OpenConnection();
        using var command = new PgSqlCommand("SELECT @p", connection)
        {
            Parameters = { new PgSqlParameter<int?>("p", null) }
        };
        using var reader = command.ExecuteReader();

        //Assert
        reader.Read().Should().BeTrue();
        reader.GetFieldValue<int?>(0).Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DBNull_reuses_type_info(bool generic)
    {
        var param = generic ? new PgSqlParameter<object> { Value = "value" } : new PgSqlParameter { Value = "value" };
        param.ResolveTypeInfo(DataSource.CurrentReloadableState.SerializerOptions, null);
        param.GetResolutionInfo(out var typeInfo, out _, out _);
        typeInfo.Should().NotBeNull();

        // Make sure we don't reset the type info when setting DBNull.
        param.Value = DBNull.Value;
        param.GetResolutionInfo(out var secondTypeInfo, out _, out _);
        secondTypeInfo.Should().BeSameAs(typeInfo);

        // Make sure we don't resolve a different type info either.
        param.ResolveTypeInfo(DataSource.CurrentReloadableState.SerializerOptions, null);
        param.GetResolutionInfo(out var thirdTypeInfo, out _, out _);
        thirdTypeInfo.Should().BeSameAs(secondTypeInfo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DBNull_followed_by_non_null_reresolves(bool generic)
    {
        var param = generic ? new PgSqlParameter<object> { Value = DBNull.Value } : new PgSqlParameter { Value = DBNull.Value };
        param.ResolveTypeInfo(DataSource.CurrentReloadableState.SerializerOptions, null);
        param.GetResolutionInfo(out var typeInfo, out _, out var pgTypeId);
        typeInfo.Should().NotBeNull();
        pgTypeId.IsUnspecified.Should().BeTrue();

        param.Value = "value";
        param.GetResolutionInfo(out var secondTypeInfo, out _, out _);
        secondTypeInfo.Should().BeNull();

        // Make sure we don't resolve the same type info either.
        param.ResolveTypeInfo(DataSource.CurrentReloadableState.SerializerOptions, null);
        param.GetResolutionInfo(out var thirdTypeInfo, out _, out _);
        thirdTypeInfo.Should().NotBeSameAs(typeInfo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void changing_value_type_reresolves(bool generic)
    {
        var param = generic ? new PgSqlParameter<object> { Value = "value" } : new PgSqlParameter { Value = "value" };
        param.ResolveTypeInfo(DataSource.CurrentReloadableState.SerializerOptions, null);
        param.GetResolutionInfo(out var typeInfo, out _, out _);
        typeInfo.Should().NotBeNull();

        param.Value = 1;
        param.GetResolutionInfo(out var secondTypeInfo, out _, out _);
        secondTypeInfo.Should().BeNull();

        // Make sure we don't resolve a different type info either.
        param.ResolveTypeInfo(DataSource.CurrentReloadableState.SerializerOptions, null);
        param.GetResolutionInfo(out var thirdTypeInfo, out _, out _);
        thirdTypeInfo.Should().NotBeSameAs(typeInfo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DataTypeName_prioritized_over_PgSqlDbType(bool generic)
    {
        //Arrange
        var param = generic ? new PgSqlParameter<object>
        {
            PgSqlDbType = PgSqlDbType.Integer,
            DataTypeName = "text",
            Value = "value"
        } : new PgSqlParameter
        {
            PgSqlDbType = PgSqlDbType.Integer,
            DataTypeName = "text",
            Value = "value"
        };
        param.ResolveTypeInfo(DataSource.CurrentReloadableState.SerializerOptions, null);
        param.GetResolutionInfo(out var typeInfo, out _, out _);

        //Assert
        typeInfo.Should().NotBeNull();
        typeInfo.PgTypeId.Should().Be(DataSource.CurrentReloadableState.SerializerOptions.TextPgTypeId);
    }
}

/// <summary>
/// Bootstraps the default data source once for <see cref="PgSqlParameterTest"/>, whose type-resolution tests read its
/// reloadable state synchronously.
/// </summary>
public sealed class PgSqlParameterTestFixture : TestBase, IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        // Bootstrap datasource.
        await using (var _ = await OpenConnectionAsync()) {}
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
