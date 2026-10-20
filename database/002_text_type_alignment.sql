-- These six columns were widened in the successful copy transaction. This
-- migration records the final type alignment and works for a fresh schema too.
ALTER TABLE dbo.contacts ALTER COLUMN name nvarchar(max) NOT NULL;
ALTER TABLE dbo.knowledge_articles ALTER COLUMN title nvarchar(max) NOT NULL;
ALTER TABLE dbo.knowledge_articles ALTER COLUMN body nvarchar(max) NOT NULL;
ALTER TABLE dbo.ticket_comments ALTER COLUMN body nvarchar(max) NOT NULL;
ALTER TABLE dbo.tickets ALTER COLUMN subject nvarchar(max) NOT NULL;
ALTER TABLE dbo.tickets ALTER COLUMN description nvarchar(max) NOT NULL;
GO
