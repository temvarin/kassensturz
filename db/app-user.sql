-- Lets the Web App's managed identity use the documents, and nothing else: read, create and change them, but not
-- delete them, change the table, or touch the history. Run once as admin, in the database (not in master).
-- The user's name is the Web App's name.
CREATE USER [kassensturz-temvarin] FROM EXTERNAL PROVIDER;
GRANT SELECT, INSERT, UPDATE ON dbo.Documents TO [kassensturz-temvarin];
