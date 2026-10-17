-- Puts a document back the way it was at an earlier moment, e.g. before an entry was deleted by mistake.
-- Run as admin. The phones pick up the restored document with their next sync.
--
-- 1. Find the moment. Every version of the document, oldest first (times are UTC):
--      SELECT Version, ValidFrom, ValidTo, Json FROM dbo.Documents FOR SYSTEM_TIME ALL WHERE Id = N'kindergeld' ORDER BY ValidFrom;
-- 2. Fill in the document and the moment below, then run this script.
--    The restore is a new version itself, so it can be undone the same way.
DECLARE @Id nvarchar(200) = N'kindergeld', @At datetime2 = '2026-10-17T12:00:00';

UPDATE latest
SET Json = earlier.Json, Version = latest.Version + 1
FROM dbo.Documents AS latest
JOIN dbo.Documents FOR SYSTEM_TIME AS OF @At AS earlier ON earlier.Id = latest.Id
WHERE latest.Id = @Id;

SELECT @@ROWCOUNT AS Restored;
