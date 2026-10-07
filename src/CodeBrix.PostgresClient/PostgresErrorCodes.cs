using System;

namespace CodeBrix.PostgresClient; //was previously: Npgsql;

/// <summary>
/// Provides constants for PostgreSQL error codes.
/// </summary>
/// <remarks>
/// See https://www.postgresql.org/docs/current/static/errcodes-appendix.html
/// </remarks>
public static class PostgresErrorCodes
{
    #region Class 00 - Successful Completion

    /// <summary>SQLSTATE 00000 (successful_completion): the command completed successfully.</summary>
    public const string SuccessfulCompletion = "00000";

    #endregion Class 00 - Successful Completion

    #region Class 01 - Warning

    /// <summary>SQLSTATE 01000 (warning): a generic warning was raised.</summary>
    public const string Warning = "01000";
    /// <summary>SQLSTATE 0100C (dynamic_result_sets_returned): dynamic result sets were returned.</summary>
    public const string DynamicResultSetsReturnedWarning = "0100C";
    /// <summary>SQLSTATE 01008 (implicit_zero_bit_padding): a bit string was implicitly padded with zero bits.</summary>
    public const string ImplicitZeroBitPaddingWarning = "01008";
    /// <summary>SQLSTATE 01003 (null_value_eliminated_in_set_function): null values were eliminated from the input of a set (aggregate) function.</summary>
    public const string NullValueEliminatedInSetFunctionWarning = "01003";
    /// <summary>SQLSTATE 01007 (privilege_not_granted): a GRANT did not grant some or all of the requested privileges.</summary>
    public const string PrivilegeNotGrantedWarning = "01007";
    /// <summary>SQLSTATE 01006 (privilege_not_revoked): a REVOKE did not revoke some or all of the requested privileges.</summary>
    public const string PrivilegeNotRevokedWarning = "01006";
    /// <summary>SQLSTATE 01004 (string_data_right_truncation): string data was truncated on the right.</summary>
    public const string StringDataRightTruncationWarning = "01004";
    /// <summary>SQLSTATE 01P01 (deprecated_feature): a deprecated feature was used.</summary>
    public const string DeprecatedFeatureWarning = "01P01";

    #endregion Class 01 - Warning

    #region Class 02 - No Data

    /// <summary>SQLSTATE 02000 (no_data): the statement found no data (e.g. no row was affected or returned).</summary>
    public const string NoData = "02000";
    /// <summary>SQLSTATE 02001 (no_additional_dynamic_result_sets_returned): no further dynamic result sets are available.</summary>
    public const string NoAdditionalDynamicResultSetsReturned = "02001";

    #endregion Class 02 - No Data

    #region Class 03 - SQL Statement Not Yet Complete

    /// <summary>SQLSTATE 03000 (sql_statement_not_yet_complete): the SQL statement has not yet completed.</summary>
    public const string SqlStatementNotYetComplete = "03000";

    #endregion Class 03 - SQL Statement Not Yet Complete

    #region Class 08 - Connection Exception

    /// <summary>SQLSTATE 08000 (connection_exception): a generic connection error occurred.</summary>
    public const string ConnectionException = "08000";
    /// <summary>SQLSTATE 08003 (connection_does_not_exist): the referenced connection does not exist.</summary>
    public const string ConnectionDoesNotExist = "08003";
    /// <summary>SQLSTATE 08006 (connection_failure): the connection to the server failed.</summary>
    public const string ConnectionFailure = "08006";
    /// <summary>SQLSTATE 08001 (sqlclient_unable_to_establish_sqlconnection): the client was unable to establish the connection.</summary>
    public const string SqlClientUnableToEstablishSqlConnection = "08001";
    /// <summary>SQLSTATE 08004 (sqlserver_rejected_establishment_of_sqlconnection): the server rejected the connection attempt.</summary>
    public const string SqlServerRejectedEstablishmentOfSqlConnection = "08004";
    /// <summary>SQLSTATE 08007 (transaction_resolution_unknown): the connection was lost and the outcome of the transaction is unknown.</summary>
    public const string TransactionResolutionUnknown = "08007";
    /// <summary>SQLSTATE 08P01 (protocol_violation): a frontend/backend wire protocol violation was detected.</summary>
    public const string ProtocolViolation = "08P01";

    #endregion Class 08 - Connection Exception

    #region Class 09 - Triggered Action Exception

    /// <summary>SQLSTATE 09000 (triggered_action_exception): an error occurred in a triggered action.</summary>
    public const string TriggeredActionException = "09000";

    #endregion Class 09 - Triggered Action Exception

    #region Class 0A - Feature Not Supported

    /// <summary>SQLSTATE 0A000 (feature_not_supported): the requested feature is not supported.</summary>
    public const string FeatureNotSupported = "0A000";

    #endregion Class 0A - Feature Not Supported

    #region Class 0B - Invalid Transaction Initiation

    /// <summary>SQLSTATE 0B000 (invalid_transaction_initiation): a transaction cannot be started in the current context.</summary>
    public const string InvalidTransactionInitiation = "0B000";

    #endregion Class 0B - Invalid Transaction Initiation

    #region Class 0F - Locator Exception

    /// <summary>SQLSTATE 0F000 (locator_exception): a generic locator error occurred.</summary>
    public const string LocatorException = "0F000";
    /// <summary>SQLSTATE 0F001 (invalid_locator_specification): the locator specification is invalid.</summary>
    public const string InvalidLocatorSpecification = "0F001";

    #endregion Class 0F - Locator Exception

    #region Class 0L - Invalid Grantor

    /// <summary>SQLSTATE 0L000 (invalid_grantor): the grantor is not valid for the privilege operation.</summary>
    public const string InvalidGrantor = "0L000";
    /// <summary>SQLSTATE 0LP01 (invalid_grant_operation): the GRANT or REVOKE operation is invalid.</summary>
    public const string InvalidGrantOperation = "0LP01";

    #endregion Class 0L - Invalid Grantor

    #region Class 0P - Invalid Role Specification

    /// <summary>SQLSTATE 0P000 (invalid_role_specification): the specified role is invalid.</summary>
    public const string InvalidRoleSpecification = "0P000";

    #endregion Class 0P - Invalid Role Specification

    #region Class 0Z - Diagnostics Exception

    /// <summary>SQLSTATE 0Z000 (diagnostics_exception): a generic diagnostics error occurred.</summary>
    public const string DiagnosticsException = "0Z000";
    /// <summary>SQLSTATE 0Z002 (stacked_diagnostics_accessed_without_active_handler): GET STACKED DIAGNOSTICS was used outside an exception handler.</summary>
    public const string StackedDiagnosticsAccessedWithoutActiveHandler = "0Z002";

    #endregion Class 0Z - Diagnostics Exception

    #region Class 20 - Case Not Found

    /// <summary>SQLSTATE 20000 (case_not_found): no branch of a CASE statement matched and there was no ELSE.</summary>
    public const string CaseNotFound = "20000";

    #endregion Class 20 - Case Not Found

    #region Class 21 - CardinalityViolation

    /// <summary>SQLSTATE 21000 (cardinality_violation): a subquery or expression returned more rows than allowed (e.g. more than one row).</summary>
    public const string CardinalityViolation = "21000";

    #endregion Class 21 - CardinalityViolation

    #region Class 22 - Data Exception

    /// <summary>SQLSTATE 22000 (data_exception): a generic data error occurred.</summary>
    public const string DataException = "22000";
    /// <summary>SQLSTATE 2202E (array_subscript_error): an array subscript is out of range or otherwise invalid.</summary>
    public const string ArraySubscriptError = "2202E";
    /// <summary>SQLSTATE 22021 (character_not_in_repertoire): a character is not valid in the target encoding.</summary>
    public const string CharacterNotInRepertoire = "22021";
    /// <summary>SQLSTATE 22008 (datetime_field_overflow): a date/time field value is out of range.</summary>
    public const string DatetimeFieldOverflow = "22008";
    /// <summary>SQLSTATE 22012 (division_by_zero): a division by zero was attempted.</summary>
    public const string DivisionByZero = "22012";
    /// <summary>SQLSTATE 22005 (error_in_assignment): an error occurred while assigning a value.</summary>
    public const string ErrorInAssignment = "22005";
    /// <summary>SQLSTATE 2200B (escape_character_conflict): the escape character conflicts with another special character.</summary>
    public const string EscapeCharacterConflict = "2200B";
    /// <summary>SQLSTATE 22022 (indicator_overflow): an indicator value overflowed.</summary>
    public const string IndicatorOverflow = "22022";
    /// <summary>SQLSTATE 22015 (interval_field_overflow): an interval field value is out of range.</summary>
    public const string IntervalFieldOverflow = "22015";
    /// <summary>SQLSTATE 2201E (invalid_argument_for_logarithm): a logarithm function was given an invalid (zero or negative) argument.</summary>
    public const string InvalidArgumentForLogarithm = "2201E";
    /// <summary>SQLSTATE 22014 (invalid_argument_for_ntile_function): the ntile() window function was given an invalid bucket count.</summary>
    public const string InvalidArgumentForNtileFunction = "22014";
    /// <summary>SQLSTATE 22016 (invalid_argument_for_nth_value_function): the nth_value() window function was given an invalid position.</summary>
    public const string InvalidArgumentForNthValueFunction = "22016";
    /// <summary>SQLSTATE 2201F (invalid_argument_for_power_function): a power function was given an invalid combination of arguments.</summary>
    public const string InvalidArgumentForPowerFunction = "2201F";
    /// <summary>SQLSTATE 2201G (invalid_argument_for_width_bucket_function): the width_bucket() function was given invalid arguments.</summary>
    public const string InvalidArgumentForWidthBucketFunction = "2201G";
    /// <summary>SQLSTATE 22018 (invalid_character_value_for_cast): a character value could not be cast to the target type.</summary>
    public const string InvalidCharacterValueForCast = "22018";
    /// <summary>SQLSTATE 22007 (invalid_datetime_format): a date/time value has an invalid format.</summary>
    public const string InvalidDatetimeFormat = "22007";
    /// <summary>SQLSTATE 22019 (invalid_escape_character): the specified escape character is invalid.</summary>
    public const string InvalidEscapeCharacter = "22019";
    /// <summary>SQLSTATE 2200D (invalid_escape_octet): an escape octet is invalid.</summary>
    public const string InvalidEscapeOctet = "2200D";
    /// <summary>SQLSTATE 22025 (invalid_escape_sequence): a string contains an invalid escape sequence.</summary>
    public const string InvalidEscapeSequence = "22025";
    /// <summary>SQLSTATE 22P06 (nonstandard_use_of_escape_character): a backslash escape was used in an ordinary string literal.</summary>
    public const string NonstandardUseOfEscapeCharacter = "22P06";
    /// <summary>SQLSTATE 22010 (invalid_indicator_parameter_value): an indicator parameter value is invalid.</summary>
    public const string InvalidIndicatorParameterValue = "22010";
    /// <summary>SQLSTATE 22023 (invalid_parameter_value): a function or setting was given an invalid parameter value.</summary>
    public const string InvalidParameterValue = "22023";
    /// <summary>SQLSTATE 2201B (invalid_regular_expression): a regular expression is invalid.</summary>
    public const string InvalidRegularExpression = "2201B";
    /// <summary>SQLSTATE 2201W (invalid_row_count_in_limit_clause): the LIMIT clause row count is invalid (e.g. negative).</summary>
    public const string InvalidRowCountInLimitClause = "2201W";
    /// <summary>SQLSTATE 2201X (invalid_row_count_in_result_offset_clause): the OFFSET clause row count is invalid (e.g. negative).</summary>
    public const string InvalidRowCountInResultOffsetClause = "2201X";
    /// <summary>SQLSTATE 2202H (invalid_tablesample_argument): a TABLESAMPLE method was given an invalid argument.</summary>
    public const string InvalidTablesampleArgument = "2202H";
    /// <summary>SQLSTATE 2202G (invalid_tablesample_repeat): the TABLESAMPLE REPEATABLE seed is invalid.</summary>
    public const string InvalidTablesampleRepeat = "2202G";
    /// <summary>SQLSTATE 22009 (invalid_time_zone_displacement_value): a time zone displacement value is invalid.</summary>
    public const string InvalidTimeZoneDisplacementValue = "22009";
    /// <summary>SQLSTATE 2200C (invalid_use_of_escape_character): an escape character was used incorrectly.</summary>
    public const string InvalidUseOfEscapeCharacter = "2200C";
    /// <summary>SQLSTATE 2200G (most_specific_type_mismatch): a value does not match the most specific required type.</summary>
    public const string MostSpecificTypeMismatch = "2200G";
    /// <summary>SQLSTATE 22004 (null_value_not_allowed): a null value was supplied where nulls are not allowed.</summary>
    public const string NullValueNotAllowed = "22004";
    /// <summary>SQLSTATE 22002 (null_value_no_indicator_parameter): a null value was returned with no indicator parameter.</summary>
    public const string NullValueNoIndicatorParameter = "22002";
    /// <summary>SQLSTATE 22003 (numeric_value_out_of_range): a numeric value is out of range for its type.</summary>
    public const string NumericValueOutOfRange = "22003";
    /// <summary>SQLSTATE 22026 (string_data_length_mismatch): a string's length does not match the required length.</summary>
    public const string StringDataLengthMismatch = "22026";
    /// <summary>SQLSTATE 22001 (string_data_right_truncation): a string value is too long for the target type.</summary>
    public const string StringDataRightTruncation = "22001";
    /// <summary>SQLSTATE 22011 (substring_error): a substring operation was given invalid arguments.</summary>
    public const string SubstringError = "22011";
    /// <summary>SQLSTATE 22027 (trim_error): a trim operation was given invalid arguments.</summary>
    public const string TrimError = "22027";
    /// <summary>SQLSTATE 22024 (unterminated_c_string): a C-style string is not null-terminated.</summary>
    public const string UnterminatedCString = "22024";
    /// <summary>SQLSTATE 2200F (zero_length_character_string): a zero-length character string was supplied where it is not allowed.</summary>
    public const string ZeroLengthCharacterString = "2200F";
    /// <summary>SQLSTATE 22P01 (floating_point_exception): a floating-point operation failed (overflow, underflow or invalid operation).</summary>
    public const string FloatingPointException = "22P01";
    /// <summary>SQLSTATE 22P02 (invalid_text_representation): a value's text representation is invalid for its type.</summary>
    public const string InvalidTextRepresentation = "22P02";
    /// <summary>SQLSTATE 22P03 (invalid_binary_representation): a value's binary representation is invalid for its type.</summary>
    public const string InvalidBinaryRepresentation = "22P03";
    /// <summary>SQLSTATE 22P04 (bad_copy_file_format): the data supplied to COPY is malformed.</summary>
    public const string BadCopyFileFormat = "22P04";
    /// <summary>SQLSTATE 22P05 (untranslatable_character): a character has no equivalent in the target encoding.</summary>
    public const string UntranslatableCharacter = "22P05";
    /// <summary>SQLSTATE 2200L (not_an_xml_document): a value is not a well-formed XML document.</summary>
    public const string NotAnXmlDocument = "2200L";
    /// <summary>SQLSTATE 2200M (invalid_xml_document): an XML document is invalid.</summary>
    public const string InvalidXmlDocument = "2200M";
    /// <summary>SQLSTATE 2200N (invalid_xml_content): XML content is invalid.</summary>
    public const string InvalidXmlContent = "2200N";
    /// <summary>SQLSTATE 2200S (invalid_xml_comment): an XML comment is invalid.</summary>
    public const string InvalidXmlComment = "2200S";
    /// <summary>SQLSTATE 2200T (invalid_xml_processing_instruction): an XML processing instruction is invalid.</summary>
    public const string InvalidXmlProcessingInstruction = "2200T";

    #endregion Class 22 - Data Exception

    #region Class 23 - Integrity Constraint Violation

    /// <summary>SQLSTATE 23000 (integrity_constraint_violation): a generic integrity constraint was violated.</summary>
    public const string IntegrityConstraintViolation = "23000";
    /// <summary>SQLSTATE 23001 (restrict_violation): a RESTRICT foreign key action prevented the change.</summary>
    public const string RestrictViolation = "23001";
    /// <summary>SQLSTATE 23502 (not_null_violation): a NOT NULL constraint was violated.</summary>
    public const string NotNullViolation = "23502";
    /// <summary>SQLSTATE 23503 (foreign_key_violation): a foreign key constraint was violated.</summary>
    public const string ForeignKeyViolation = "23503";
    /// <summary>SQLSTATE 23505 (unique_violation): a unique constraint was violated.</summary>
    public const string UniqueViolation = "23505";
    /// <summary>SQLSTATE 23514 (check_violation): a CHECK constraint was violated.</summary>
    public const string CheckViolation = "23514";
    /// <summary>SQLSTATE 23P01 (exclusion_violation): an exclusion constraint was violated.</summary>
    public const string ExclusionViolation = "23P01";

    #endregion Class 23 - Integrity Constraint Violation

    #region Class 24 - Invalid Cursor State

    /// <summary>SQLSTATE 24000 (invalid_cursor_state): the cursor is not in a valid state for the operation.</summary>
    public const string InvalidCursorState = "24000";

    #endregion Class 24 - Invalid Cursor State

    #region Class 25 - Invalid Transaction State

    /// <summary>SQLSTATE 25000 (invalid_transaction_state): the transaction is not in a valid state for the operation.</summary>
    public const string InvalidTransactionState = "25000";
    /// <summary>SQLSTATE 25001 (active_sql_transaction): the command cannot run inside an active transaction block.</summary>
    public const string ActiveSqlTransaction = "25001";
    /// <summary>SQLSTATE 25002 (branch_transaction_already_active): a branch transaction is already active.</summary>
    public const string BranchTransactionAlreadyActive = "25002";
    /// <summary>SQLSTATE 25008 (held_cursor_requires_same_isolation_level): a held cursor requires the same isolation level.</summary>
    public const string HeldCursorRequiresSameIsolationLevel = "25008";
    /// <summary>SQLSTATE 25003 (inappropriate_access_mode_for_branch_transaction): the access mode is inappropriate for a branch transaction.</summary>
    public const string InappropriateAccessModeForBranchTransaction = "25003";
    /// <summary>SQLSTATE 25004 (inappropriate_isolation_level_for_branch_transaction): the isolation level is inappropriate for a branch transaction.</summary>
    public const string InappropriateIsolationLevelForBranchTransaction = "25004";
    /// <summary>SQLSTATE 25005 (no_active_sql_transaction_for_branch_transaction): there is no active transaction for the branch transaction.</summary>
    public const string NoActiveSqlTransactionForBranchTransaction = "25005";
    /// <summary>SQLSTATE 25006 (read_only_sql_transaction): a write was attempted in a read-only transaction.</summary>
    public const string ReadOnlySqlTransaction = "25006";
    /// <summary>SQLSTATE 25007 (schema_and_data_statement_mixing_not_supported): mixing schema and data statements is not supported.</summary>
    public const string SchemaAndDataStatementMixingNotSupported = "25007";
    /// <summary>SQLSTATE 25P01 (no_active_sql_transaction): the command requires a transaction block but none is active.</summary>
    public const string NoActiveSqlTransaction = "25P01";
    /// <summary>SQLSTATE 25P02 (in_failed_sql_transaction): the current transaction is aborted; commands are ignored until the end of the transaction block.</summary>
    public const string InFailedSqlTransaction = "25P02";

    #endregion Class 25 - Invalid Transaction State

    #region Class 26 - Invalid SQL Statement Name

    /// <summary>SQLSTATE 26000 (invalid_sql_statement_name): the named prepared statement does not exist.</summary>
    public const string InvalidSqlStatementName = "26000";

    #endregion Class 26 - Invalid SQL Statement Name

    #region Class 27 - Triggered Data Change Violation

    /// <summary>SQLSTATE 27000 (triggered_data_change_violation): a trigger made a conflicting data change.</summary>
    public const string TriggeredDataChangeViolation = "27000";

    #endregion Class 27 - Triggered Data Change Violation

    #region Class 28 - Invalid Authorization Scheme

    /// <summary>SQLSTATE 28000 (invalid_authorization_specification): authentication failed or the authorization specification is invalid.</summary>
    public const string InvalidAuthorizationSpecification = "28000";
    /// <summary>SQLSTATE 28P01 (invalid_password): password authentication failed.</summary>
    public const string InvalidPassword = "28P01";

    #endregion Class 28 - Invalid Authorization Scheme

    #region Class 2B - Dependent Privilege Descriptors Still Exist

    /// <summary>SQLSTATE 2B000 (dependent_privilege_descriptors_still_exist): dependent privilege descriptors still exist.</summary>
    public const string DependentPrivilegeDescriptorsStillExist = "2B000";
    /// <summary>SQLSTATE 2BP01 (dependent_objects_still_exist): the object cannot be dropped because other objects depend on it.</summary>
    public const string DependentObjectsStillExist = "2BP01";

    #endregion Class 2B - Dependent Privilege Descriptors Still Exist

    #region Class 2D - Invalid Transaction Termination

    /// <summary>SQLSTATE 2D000 (invalid_transaction_termination): the transaction cannot be ended in the current context.</summary>
    public const string InvalidTransactionTermination = "2D000";

    #endregion Class 2D - Invalid Transaction Termination

    #region Class 2F - SQL Routine Exception

    /// <summary>SQLSTATE 2F000 (sql_routine_exception): a generic error occurred in an SQL routine.</summary>
    public const string SqlRoutineException = "2F000";
    /// <summary>SQLSTATE 2F005 (function_executed_no_return_statement): a function reached its end without executing a RETURN statement.</summary>
    public const string FunctionExecutedNoReturnStatementSqlRoutineException = "2F005";
    /// <summary>SQLSTATE 2F002 (modifying_sql_data_not_permitted): an SQL routine attempted to modify data when not permitted.</summary>
    public const string ModifyingSqlDataNotPermittedSqlRoutineException = "2F002";
    /// <summary>SQLSTATE 2F003 (prohibited_sql_statement_attempted): an SQL routine attempted a prohibited SQL statement.</summary>
    public const string ProhibitedSqlStatementAttemptedSqlRoutineException = "2F003";
    /// <summary>SQLSTATE 2F004 (reading_sql_data_not_permitted): an SQL routine attempted to read data when not permitted.</summary>
    public const string ReadingSqlDataNotPermittedSqlRoutineException = "2F004";

    #endregion Class 2F - SQL Routine Exception

    #region Class 34 - Invalid Cursor Name

    /// <summary>SQLSTATE 34000 (invalid_cursor_name): the named cursor does not exist.</summary>
    public const string InvalidCursorName = "34000";

    #endregion Class 34 - Invalid Cursor Name

    #region Class 38 - External Routine Exception

    /// <summary>SQLSTATE 38000 (external_routine_exception): a generic error occurred in an external routine.</summary>
    public const string ExternalRoutineException = "38000";
    /// <summary>SQLSTATE 38001 (containing_sql_not_permitted): an external routine attempted to contain SQL when not permitted.</summary>
    public const string ContainingSqlNotPermittedExternalRoutineException = "38001";
    /// <summary>SQLSTATE 38002 (modifying_sql_data_not_permitted): an external routine attempted to modify data when not permitted.</summary>
    public const string ModifyingSqlDataNotPermittedExternalRoutineException = "38002";
    /// <summary>SQLSTATE 38003 (prohibited_sql_statement_attempted): an external routine attempted a prohibited SQL statement.</summary>
    public const string ProhibitedSqlStatementAttemptedExternalRoutineException = "38003";
    /// <summary>SQLSTATE 38004 (reading_sql_data_not_permitted): an external routine attempted to read data when not permitted.</summary>
    public const string ReadingSqlDataNotPermittedExternalRoutineException = "38004";

    #endregion Class 38 - External Routine Exception

    #region Class 39 - External Routine Invocation Exception

    /// <summary>SQLSTATE 39000 (external_routine_invocation_exception): a generic error occurred while invoking an external routine.</summary>
    public const string ExternalRoutineInvocationException = "39000";
    /// <summary>SQLSTATE 39001 (invalid_sqlstate_returned): an external routine returned an invalid SQLSTATE.</summary>
    public const string InvalidSqlstateReturnedExternalRoutineInvocationException = "39001";
    /// <summary>SQLSTATE 39004 (null_value_not_allowed): an external routine received or returned a disallowed null value.</summary>
    public const string NullValueNotAllowedExternalRoutineInvocationException = "39004";
    /// <summary>SQLSTATE 39P01 (trigger_protocol_violated): a trigger function violated the trigger protocol.</summary>
    public const string TriggerProtocolViolatedExternalRoutineInvocationException = "39P01";
    /// <summary>SQLSTATE 39P02 (srf_protocol_violated): a set-returning function violated the SRF protocol.</summary>
    public const string SrfProtocolViolatedExternalRoutineInvocationException = "39P02";
    /// <summary>SQLSTATE 39P03 (event_trigger_protocol_violated): an event trigger function violated the event trigger protocol.</summary>
    public const string EventTriggerProtocolViolatedExternalRoutineInvocationException = "39P03";

    #endregion Class 39 - External Routine Invocation Exception

    #region Class 3B - Savepoint Exception

    /// <summary>SQLSTATE 3B000 (savepoint_exception): a generic savepoint error occurred.</summary>
    public const string SavepointException = "3B000";
    /// <summary>SQLSTATE 3B001 (invalid_savepoint_specification): the named savepoint does not exist or is invalid.</summary>
    public const string InvalidSavepointSpecification = "3B001";

    #endregion Class 3B - Savepoint Exception

    #region Class 3D - Invalid Catalog Name

    /// <summary>SQLSTATE 3D000 (invalid_catalog_name): the specified database does not exist.</summary>
    public const string InvalidCatalogName = "3D000";

    #endregion Class 3D - Invalid Catalog Name

    #region Class 3F - Invalid Schema Name

    /// <summary>SQLSTATE 3F000 (invalid_schema_name): the specified schema does not exist.</summary>
    public const string InvalidSchemaName = "3F000";

    #endregion Class 3F - Invalid Schema Name

    #region Class 40 - Transaction Rollback

    /// <summary>SQLSTATE 40000 (transaction_rollback): the transaction was rolled back.</summary>
    public const string TransactionRollback = "40000";
    /// <summary>SQLSTATE 40002 (transaction_integrity_constraint_violation): the transaction was rolled back due to an integrity constraint violation.</summary>
    public const string TransactionIntegrityConstraintViolation = "40002";
    /// <summary>SQLSTATE 40001 (serialization_failure): the transaction could not be serialized and should be retried.</summary>
    public const string SerializationFailure = "40001";
    /// <summary>SQLSTATE 40003 (statement_completion_unknown): it is unknown whether the statement completed.</summary>
    public const string StatementCompletionUnknown = "40003";
    /// <summary>SQLSTATE 40P01 (deadlock_detected): a deadlock was detected and this transaction was chosen as the victim.</summary>
    public const string DeadlockDetected = "40P01";

    #endregion Class 40 - Transaction Rollback

    #region Class 42 - Syntax Error or Access Rule Violation

    /// <summary>SQLSTATE 42000 (syntax_error_or_access_rule_violation): a generic syntax error or access rule violation occurred.</summary>
    public const string SyntaxErrorOrAccessRuleViolation = "42000";
    /// <summary>SQLSTATE 42601 (syntax_error): the SQL statement contains a syntax error.</summary>
    public const string SyntaxError = "42601";
    /// <summary>SQLSTATE 42501 (insufficient_privilege): the current role lacks the privilege required for the operation.</summary>
    public const string InsufficientPrivilege = "42501";
    /// <summary>SQLSTATE 42846 (cannot_coerce): a value cannot be coerced to the required type.</summary>
    public const string CannotCoerce = "42846";
    /// <summary>SQLSTATE 42803 (grouping_error): a column must appear in GROUP BY or be used in an aggregate function.</summary>
    public const string GroupingError = "42803";
    /// <summary>SQLSTATE 42P20 (windowing_error): a window function was used incorrectly.</summary>
    public const string WindowingError = "42P20";
    /// <summary>SQLSTATE 42P19 (invalid_recursion): a recursive query is invalid.</summary>
    public const string InvalidRecursion = "42P19";
    /// <summary>SQLSTATE 42830 (invalid_foreign_key): a foreign key definition is invalid (e.g. no matching unique constraint).</summary>
    public const string InvalidForeignKey = "42830";
    /// <summary>SQLSTATE 42602 (invalid_name): a name is invalid.</summary>
    public const string InvalidName = "42602";
    /// <summary>SQLSTATE 42622 (name_too_long): an identifier exceeds the maximum length.</summary>
    public const string NameTooLong = "42622";
    /// <summary>SQLSTATE 42939 (reserved_name): a reserved name was used.</summary>
    public const string ReservedName = "42939";
    /// <summary>SQLSTATE 42804 (datatype_mismatch): the data types of an expression do not match.</summary>
    public const string DatatypeMismatch = "42804";
    /// <summary>SQLSTATE 42P18 (indeterminate_datatype): the data type of a parameter or expression could not be determined.</summary>
    public const string IndeterminateDatatype = "42P18";
    /// <summary>SQLSTATE 42P21 (collation_mismatch): conflicting collations were found.</summary>
    public const string CollationMismatch = "42P21";
    /// <summary>SQLSTATE 42P22 (indeterminate_collation): the collation to use could not be determined.</summary>
    public const string IndeterminateCollation = "42P22";
    /// <summary>SQLSTATE 42809 (wrong_object_type): the object is not of the type required by the operation.</summary>
    public const string WrongObjectType = "42809";
    /// <summary>SQLSTATE 42703 (undefined_column): the referenced column does not exist.</summary>
    public const string UndefinedColumn = "42703";
    /// <summary>SQLSTATE 42883 (undefined_function): no function matches the given name and argument types.</summary>
    public const string UndefinedFunction = "42883";
    /// <summary>SQLSTATE 42P01 (undefined_table): the referenced table or relation does not exist.</summary>
    public const string UndefinedTable = "42P01";
    /// <summary>SQLSTATE 42P02 (undefined_parameter): the referenced parameter does not exist.</summary>
    public const string UndefinedParameter = "42P02";
    /// <summary>SQLSTATE 42704 (undefined_object): the referenced object does not exist.</summary>
    public const string UndefinedObject = "42704";
    /// <summary>SQLSTATE 42701 (duplicate_column): a column with the same name already exists.</summary>
    public const string DuplicateColumn = "42701";
    /// <summary>SQLSTATE 42P03 (duplicate_cursor): a cursor with the same name already exists.</summary>
    public const string DuplicateCursor = "42P03";
    /// <summary>SQLSTATE 42P04 (duplicate_database): a database with the same name already exists.</summary>
    public const string DuplicateDatabase = "42P04";
    /// <summary>SQLSTATE 42723 (duplicate_function): a function with the same name and argument types already exists.</summary>
    public const string DuplicateFunction = "42723";
    /// <summary>SQLSTATE 42P05 (duplicate_prepared_statement): a prepared statement with the same name already exists.</summary>
    public const string DuplicatePreparedStatement = "42P05";
    /// <summary>SQLSTATE 42P06 (duplicate_schema): a schema with the same name already exists.</summary>
    public const string DuplicateSchema = "42P06";
    /// <summary>SQLSTATE 42P07 (duplicate_table): a table or relation with the same name already exists.</summary>
    public const string DuplicateTable = "42P07";
    /// <summary>SQLSTATE 42712 (duplicate_alias): the same table alias was specified more than once.</summary>
    public const string DuplicateAlias = "42712";
    /// <summary>SQLSTATE 42710 (duplicate_object): an object with the same name already exists.</summary>
    public const string DuplicateObject = "42710";
    /// <summary>SQLSTATE 42702 (ambiguous_column): a column reference is ambiguous.</summary>
    public const string AmbiguousColumn = "42702";
    /// <summary>SQLSTATE 42725 (ambiguous_function): a function call matches more than one candidate function.</summary>
    public const string AmbiguousFunction = "42725";
    /// <summary>SQLSTATE 42P08 (ambiguous_parameter): a parameter reference is ambiguous.</summary>
    public const string AmbiguousParameter = "42P08";
    /// <summary>SQLSTATE 42P09 (ambiguous_alias): a table alias is ambiguous.</summary>
    public const string AmbiguousAlias = "42P09";
    /// <summary>SQLSTATE 42P10 (invalid_column_reference): a column reference is invalid in this context.</summary>
    public const string InvalidColumnReference = "42P10";
    /// <summary>SQLSTATE 42611 (invalid_column_definition): a column definition is invalid.</summary>
    public const string InvalidColumnDefinition = "42611";
    /// <summary>SQLSTATE 42P11 (invalid_cursor_definition): a cursor definition is invalid.</summary>
    public const string InvalidCursorDefinition = "42P11";
    /// <summary>SQLSTATE 42P12 (invalid_database_definition): a database definition is invalid.</summary>
    public const string InvalidDatabaseDefinition = "42P12";
    /// <summary>SQLSTATE 42P13 (invalid_function_definition): a function definition is invalid.</summary>
    public const string InvalidFunctionDefinition = "42P13";
    /// <summary>SQLSTATE 42P14 (invalid_prepared_statement_definition): a prepared statement definition is invalid.</summary>
    public const string InvalidPreparedStatementDefinition = "42P14";
    /// <summary>SQLSTATE 42P15 (invalid_schema_definition): a schema definition is invalid.</summary>
    public const string InvalidSchemaDefinition = "42P15";
    /// <summary>SQLSTATE 42P16 (invalid_table_definition): a table definition is invalid.</summary>
    public const string InvalidTableDefinition = "42P16";
    /// <summary>SQLSTATE 42P17 (invalid_object_definition): an object definition is invalid.</summary>
    public const string InvalidObjectDefinition = "42P17";

    #endregion Class 42 - Syntax Error or Access Rule Violation

    #region Class 44 - WITH CHECK OPTION Violation

    /// <summary>SQLSTATE 44000 (with_check_option_violation): a row violates the WITH CHECK OPTION of a view or a row-level security policy.</summary>
    public const string WithCheckOptionViolation = "44000";

    #endregion Class 44 - WITH CHECK OPTION Violation

    #region Class 53 - Insufficient Resources

    /// <summary>SQLSTATE 53000 (insufficient_resources): the server has insufficient resources to complete the operation.</summary>
    public const string InsufficientResources = "53000";
    /// <summary>SQLSTATE 53100 (disk_full): the server ran out of disk space.</summary>
    public const string DiskFull = "53100";
    /// <summary>SQLSTATE 53200 (out_of_memory): the server ran out of memory.</summary>
    public const string OutOfMemory = "53200";
    /// <summary>SQLSTATE 53300 (too_many_connections): the server has reached its maximum number of connections.</summary>
    public const string TooManyConnections = "53300";
    /// <summary>SQLSTATE 53400 (configuration_limit_exceeded): a configured resource limit was exceeded.</summary>
    public const string ConfigurationLimitExceeded = "53400";

    #endregion Class 53 - Insufficient Resources

    #region Class 54 - Program Limit Exceeded

    /// <summary>SQLSTATE 54000 (program_limit_exceeded): an internal program limit was exceeded.</summary>
    public const string ProgramLimitExceeded = "54000";
    /// <summary>SQLSTATE 54001 (statement_too_complex): the statement is too complex to process.</summary>
    public const string StatementTooComplex = "54001";
    /// <summary>SQLSTATE 54011 (too_many_columns): the number of columns exceeds the limit.</summary>
    public const string TooManyColumns = "54011";
    /// <summary>SQLSTATE 54023 (too_many_arguments): the number of function arguments exceeds the limit.</summary>
    public const string TooManyArguments = "54023";

    #endregion Class 54 - Program Limit Exceeded

    #region Class 55 - Object Not In Prerequisite State

    /// <summary>SQLSTATE 55000 (object_not_in_prerequisite_state): the object is not in the state required for the operation.</summary>
    public const string ObjectNotInPrerequisiteState = "55000";
    /// <summary>SQLSTATE 55006 (object_in_use): the object is in use by another session.</summary>
    public const string ObjectInUse = "55006";
    /// <summary>SQLSTATE 55P02 (cant_change_runtime_param): the configuration parameter cannot be changed at this time.</summary>
    public const string CantChangeRuntimeParam = "55P02";
    /// <summary>SQLSTATE 55P03 (lock_not_available): a requested lock could not be obtained (e.g. with NOWAIT or lock_timeout).</summary>
    public const string LockNotAvailable = "55P03";

    #endregion Class 55 - Object Not In Prerequisite State

    #region Class 57 - Operator Intervention

    /// <summary>SQLSTATE 57000 (operator_intervention): the operation was interrupted by operator intervention.</summary>
    public const string OperatorIntervention = "57000";
    /// <summary>SQLSTATE 57014 (query_canceled): the statement was canceled by user request or by statement_timeout.</summary>
    public const string QueryCanceled = "57014";
    /// <summary>SQLSTATE 57P01 (admin_shutdown): the connection was terminated by an administrator command or server shutdown.</summary>
    public const string AdminShutdown = "57P01";
    /// <summary>SQLSTATE 57P02 (crash_shutdown): the connection was terminated because the server crashed.</summary>
    public const string CrashShutdown = "57P02";
    /// <summary>SQLSTATE 57P03 (cannot_connect_now): the server is not accepting connections right now (e.g. it is starting up).</summary>
    public const string CannotConnectNow = "57P03";
    /// <summary>SQLSTATE 57P04 (database_dropped): the connection was terminated because its database was dropped.</summary>
    public const string DatabaseDropped = "57P04";
    /// <summary>SQLSTATE 57P05 (idle_session_timeout): the connection was terminated because idle_session_timeout elapsed.</summary>
    public const string IdleSessionTimeout = "57P05";

    #endregion Class 57 - Operator Intervention

    #region Class 58 - System Error (errors external to PostgreSQL itself)

    /// <summary>SQLSTATE 58000 (system_error): an error occurred in the operating system outside PostgreSQL.</summary>
    public const string SystemError = "58000";
    /// <summary>SQLSTATE 58030 (io_error): an I/O error occurred.</summary>
    public const string IoError = "58030";
    /// <summary>SQLSTATE 58P01 (undefined_file): a required file does not exist.</summary>
    public const string UndefinedFile = "58P01";
    /// <summary>SQLSTATE 58P02 (duplicate_file): a file already exists.</summary>
    public const string DuplicateFile = "58P02";

    #endregion Class 58 - System Error (errors external to PostgreSQL itself)

    #region Class 72 - Snapshot Failure

    /// <summary>SQLSTATE 72000 (snapshot_too_old): the snapshot is too old to be used.</summary>
    public const string SnapshotFailure = "72000";

    #endregion Class 72 - Snapshot Failure

    #region Class F0 - Configuration File Error

    /// <summary>SQLSTATE F0000 (config_file_error): a configuration file contains an error.</summary>
    public const string ConfigFileError = "F0000";
    /// <summary>SQLSTATE F0001 (lock_file_exists): the server lock file already exists.</summary>
    public const string LockFileExists = "F0001";

    #endregion Class F0 - Configuration File Error

    #region Class HV - Foreign Data Wrapper Error (SQL/MED)

    /// <summary>SQLSTATE HV000 (fdw_error): a generic foreign data wrapper error occurred.</summary>
    public const string FdwError = "HV000";
    /// <summary>SQLSTATE HV005 (fdw_column_name_not_found): a foreign data wrapper column name was not found.</summary>
    public const string FdwColumnNameNotFound = "HV005";
    /// <summary>SQLSTATE HV002 (fdw_dynamic_parameter_value_needed): a foreign data wrapper needs a dynamic parameter value.</summary>
    public const string FdwDynamicParameterValueNeeded = "HV002";
    /// <summary>SQLSTATE HV010 (fdw_function_sequence_error): foreign data wrapper functions were called in an invalid sequence.</summary>
    public const string FdwFunctionSequenceError = "HV010";
    /// <summary>SQLSTATE HV021 (fdw_inconsistent_descriptor_information): foreign data wrapper descriptor information is inconsistent.</summary>
    public const string FdwInconsistentDescriptorInformation = "HV021";
    /// <summary>SQLSTATE HV024 (fdw_invalid_attribute_value): a foreign data wrapper attribute value is invalid.</summary>
    public const string FdwInvalidAttributeValue = "HV024";
    /// <summary>SQLSTATE HV007 (fdw_invalid_column_name): a foreign data wrapper column name is invalid.</summary>
    public const string FdwInvalidColumnName = "HV007";
    /// <summary>SQLSTATE HV008 (fdw_invalid_column_number): a foreign data wrapper column number is invalid.</summary>
    public const string FdwInvalidColumnNumber = "HV008";
    /// <summary>SQLSTATE HV004 (fdw_invalid_data_type): a foreign data wrapper data type is invalid.</summary>
    public const string FdwInvalidDataType = "HV004";
    /// <summary>SQLSTATE HV006 (fdw_invalid_data_type_descriptors): foreign data wrapper data type descriptors are invalid.</summary>
    public const string FdwInvalidDataTypeDescriptors = "HV006";
    /// <summary>SQLSTATE HV091 (fdw_invalid_descriptor_field_identifier): a foreign data wrapper descriptor field identifier is invalid.</summary>
    public const string FdwInvalidDescriptorFieldIdentifier = "HV091";
    /// <summary>SQLSTATE HV00B (fdw_invalid_handle): a foreign data wrapper handle is invalid.</summary>
    public const string FdwInvalidHandle = "HV00B";
    /// <summary>SQLSTATE HV00C (fdw_invalid_option_index): a foreign data wrapper option index is invalid.</summary>
    public const string FdwInvalidOptionIndex = "HV00C";
    /// <summary>SQLSTATE HV00D (fdw_invalid_option_name): a foreign data wrapper option name is invalid.</summary>
    public const string FdwInvalidOptionName = "HV00D";
    /// <summary>SQLSTATE HV090 (fdw_invalid_string_length_or_buffer_length): a foreign data wrapper string or buffer length is invalid.</summary>
    public const string FdwInvalidStringLengthOrBufferLength = "HV090";
    /// <summary>SQLSTATE HV00A (fdw_invalid_string_format): a foreign data wrapper string format is invalid.</summary>
    public const string FdwInvalidStringFormat = "HV00A";
    /// <summary>SQLSTATE HV009 (fdw_invalid_use_of_null_pointer): a foreign data wrapper used a null pointer invalidly.</summary>
    public const string FdwInvalidUseOfNullPointer = "HV009";
    /// <summary>SQLSTATE HV014 (fdw_too_many_handles): a foreign data wrapper has too many handles open.</summary>
    public const string FdwTooManyHandles = "HV014";
    /// <summary>SQLSTATE HV001 (fdw_out_of_memory): a foreign data wrapper ran out of memory.</summary>
    public const string FdwOutOfMemory = "HV001";
    /// <summary>SQLSTATE HV00P (fdw_no_schemas): the foreign server has no schemas.</summary>
    public const string FdwNoSchemas = "HV00P";
    /// <summary>SQLSTATE HV00J (fdw_option_name_not_found): a foreign data wrapper option name was not found.</summary>
    public const string FdwOptionNameNotFound = "HV00J";
    /// <summary>SQLSTATE HV00K (fdw_reply_handle): a foreign data wrapper reply handle error occurred.</summary>
    public const string FdwReplyHandle = "HV00K";
    /// <summary>SQLSTATE HV00Q (fdw_schema_not_found): a foreign data wrapper schema was not found.</summary>
    public const string FdwSchemaNotFound = "HV00Q";
    /// <summary>SQLSTATE HV00R (fdw_table_not_found): a foreign data wrapper table was not found.</summary>
    public const string FdwTableNotFound = "HV00R";
    /// <summary>SQLSTATE HV00L (fdw_unable_to_create_execution): a foreign data wrapper was unable to create an execution.</summary>
    public const string FdwUnableToCreateExecution = "HV00L";
    /// <summary>SQLSTATE HV00M (fdw_unable_to_create_reply): a foreign data wrapper was unable to create a reply.</summary>
    public const string FdwUnableToCreateReply = "HV00M";
    /// <summary>SQLSTATE HV00N (fdw_unable_to_establish_connection): a foreign data wrapper was unable to connect to the foreign server.</summary>
    public const string FdwUnableToEstablishConnection = "HV00N";

    #endregion Class HV - Foreign Data Wrapper Error (SQL/MED)

    #region Class P0 - PL/pgSQL Error

    /// <summary>SQLSTATE P0000 (plpgsql_error): a generic PL/pgSQL error occurred.</summary>
    public const string PlpgsqlError = "P0000";
    /// <summary>SQLSTATE P0001 (raise_exception): a PL/pgSQL RAISE EXCEPTION statement was executed without a specific SQLSTATE.</summary>
    public const string RaiseException = "P0001";
    /// <summary>SQLSTATE P0002 (no_data_found): a PL/pgSQL SELECT INTO ... STRICT returned no rows.</summary>
    public const string NoDataFound = "P0002";
    /// <summary>SQLSTATE P0003 (too_many_rows): a PL/pgSQL SELECT INTO ... STRICT returned more than one row.</summary>
    public const string TooManyRows = "P0003";
    /// <summary>SQLSTATE P0004 (assert_failure): a PL/pgSQL ASSERT statement failed.</summary>
    public const string AssertFailure = "P0004";

    #endregion Class P0 - PL/pgSQL Error

    #region Class XX - Internal Error

    /// <summary>SQLSTATE XX000 (internal_error): an internal server error occurred.</summary>
    public const string InternalError = "XX000";
    /// <summary>SQLSTATE XX001 (data_corrupted): corrupted data was detected.</summary>
    public const string DataCorrupted = "XX001";
    /// <summary>SQLSTATE XX002 (index_corrupted): a corrupted index was detected.</summary>
    public const string IndexCorrupted = "XX002";

    #endregion Class XX - Internal Error

    static readonly string[] CriticalFailureCodes =
    [
        "53", // Insufficient resources
        AdminShutdown, // Self explanatory
        CrashShutdown, // Self explanatory
        CannotConnectNow, // Database is starting up
        "58", // System errors, external to PG (server is dying)
        "F0", // Configuration file error
        "XX" // Internal error (database is dying)
    ];

    internal static bool IsCriticalFailure(PostgresException e, bool clusterError = true)
    {
        foreach (var x in CriticalFailureCodes)
            if (e.SqlState.StartsWith(x, StringComparison.Ordinal))
                return true;

        // We only treat ProtocolViolation as critical for connection
        return !clusterError && e.SqlState == ProtocolViolation;
    }
}
