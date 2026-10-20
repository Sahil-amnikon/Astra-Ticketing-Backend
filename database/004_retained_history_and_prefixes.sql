CREATE TRIGGER dbo.change_history_immutable ON dbo.change_history AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted) THROW 51001,'Change history is immutable',1;
END;
GO
CREATE TRIGGER dbo.change_reviews_immutable ON dbo.change_reviews AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted) THROW 51001,'Change reviews are immutable',1;
END;
GO
CREATE TRIGGER dbo.clients_ticket_prefix ON dbo.clients AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF NOT UPDATE(ticket_prefix) RETURN;
 INSERT dbo.ticket_prefix_registry(prefix,owner_kind,owner_id)
 SELECT i.ticket_prefix,'CLIENT',i.id FROM inserted i
 WHERE NOT EXISTS(SELECT 1 FROM dbo.ticket_prefix_registry r WITH (UPDLOCK,HOLDLOCK) WHERE r.prefix=i.ticket_prefix);
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.ticket_prefix_registry r ON r.prefix=i.ticket_prefix WHERE r.owner_kind<>'CLIENT' OR r.owner_id<>i.id OR r.owner_id IS NULL)
  THROW 51002,'Ticket prefix is already reserved',1;
END;
GO
CREATE TRIGGER dbo.sites_ticket_prefix ON dbo.sites AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF NOT UPDATE(ticket_prefix) RETURN;
 INSERT dbo.ticket_prefix_registry(prefix,owner_kind,owner_id)
 SELECT i.ticket_prefix,'SITE',i.id FROM inserted i
 WHERE NOT EXISTS(SELECT 1 FROM dbo.ticket_prefix_registry r WITH (UPDLOCK,HOLDLOCK) WHERE r.prefix=i.ticket_prefix);
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.ticket_prefix_registry r ON r.prefix=i.ticket_prefix WHERE r.owner_kind<>'SITE' OR r.owner_id<>i.id OR r.owner_id IS NULL)
  THROW 51002,'Ticket prefix is already reserved',1;
END;
GO
