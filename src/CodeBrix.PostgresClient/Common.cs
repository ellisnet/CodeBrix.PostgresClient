namespace CodeBrix.PostgresClient; //was previously: Npgsql;

/// <summary>
/// Base class for all classes which represent a message sent by the PostgreSQL backend.
/// </summary>
interface IBackendMessage
{
    BackendMessageCode Code { get; }
}

enum BackendMessageCode : byte
{
    AuthenticationRequest = (byte)'R',
    BackendKeyData        = (byte)'K',
    BindComplete          = (byte)'2',
    CloseComplete         = (byte)'3',
    CommandComplete       = (byte)'C',
    CopyData              = (byte)'d',
    CopyDone              = (byte)'c',
    CopyBothResponse      = (byte)'W',
    CopyInResponse        = (byte)'G',
    CopyOutResponse       = (byte)'H',
    DataRow               = (byte)'D',
    EmptyQueryResponse    = (byte)'I',
    ErrorResponse         = (byte)'E',
    FunctionCall          = (byte)'F',
    FunctionCallResponse  = (byte)'V',
    NoData                = (byte)'n',
    NoticeResponse        = (byte)'N',
    NotificationResponse  = (byte)'A',
    ParameterDescription  = (byte)'t',
    ParameterStatus       = (byte)'S',
    ParseComplete         = (byte)'1',
    PasswordPacket        = (byte)' ',
    PortalSuspended       = (byte)'s',
    ReadyForQuery         = (byte)'Z',
    RowDescription        = (byte)'T',
}

static class FrontendMessageCode
{
    internal const byte Describe =  (byte)'D';
    internal const byte Sync =      (byte)'S';
    internal const byte Execute =   (byte)'E';
    internal const byte Parse =     (byte)'P';
    internal const byte Bind =      (byte)'B';
    internal const byte Close =     (byte)'C';
    internal const byte Query =     (byte)'Q';
    internal const byte CopyData =  (byte)'d';
    internal const byte CopyDone =  (byte)'c';
    internal const byte CopyFail =  (byte)'f';
    internal const byte Terminate = (byte)'X';
    internal const byte Password =  (byte)'p';
}

enum StatementOrPortal : byte
{
    Statement = (byte)'S',
    Portal = (byte)'P'
}

/// <summary>
/// Specifies the type of SQL statement, e.g. SELECT
/// </summary>
public enum StatementType
{
    /// <summary>The statement type could not be determined from the command completion tag.</summary>
    Unknown,
    /// <summary>A SELECT statement.</summary>
    Select,
    /// <summary>An INSERT statement.</summary>
    Insert,
    /// <summary>A DELETE statement.</summary>
    Delete,
    /// <summary>An UPDATE statement.</summary>
    Update,
    /// <summary>A CREATE TABLE ... AS (or SELECT INTO) statement.</summary>
    CreateTableAs,
    /// <summary>A MOVE statement, repositioning a cursor.</summary>
    Move,
    /// <summary>A FETCH statement, retrieving rows from a cursor.</summary>
    Fetch,
    /// <summary>A COPY statement.</summary>
    Copy,
    /// <summary>A statement of some other type (e.g. DDL) whose completion tag carries no row count.</summary>
    Other,
    /// <summary>A MERGE statement.</summary>
    Merge,
    /// <summary>A CALL statement, invoking a procedure.</summary>
    Call
}
