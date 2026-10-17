-- The synced documents, one row each: 'kindergeld', shared by the family, and 'budget:<user id>', one per member.
-- Run once as admin (the app itself may only read and write rows). Running it again changes nothing.
IF OBJECT_ID(N'dbo.Documents') IS NULL
CREATE TABLE dbo.Documents
(
    Id nvarchar(200) NOT NULL CONSTRAINT PK_Documents PRIMARY KEY,
    Json nvarchar(max) NOT NULL CONSTRAINT CK_Documents_Json CHECK (ISJSON(Json) = 1),
    -- Raised by one with every write. A write names the version it was based on, so a write based on an outdated
    -- version is refused instead of overwriting someone else's (compare-and-set).
    Version int NOT NULL,
    -- Kept by SQL Server: each write moves the previous version of the row into dbo.DocumentsHistory, so mistakes can
    -- be undone (db/restore.sql). The history is deleted after 6 months.
    ValidFrom datetime2 GENERATED ALWAYS AS ROW START NOT NULL,
    ValidTo datetime2 GENERATED ALWAYS AS ROW END NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.DocumentsHistory, HISTORY_RETENTION_PERIOD = 6 MONTHS));
