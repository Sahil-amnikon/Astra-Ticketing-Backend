CREATE TABLE dbo.[client_mail_routes](
  [address] nvarchar(450) NOT NULL,
  [client_id] uniqueidentifier NOT NULL,
  [active] bit DEFAULT 0 NOT NULL,
  [outbound_authorized] bit DEFAULT 0 NOT NULL,
  CONSTRAINT [client_mail_routes_pkey] PRIMARY KEY ([address])
);
GO
ALTER TABLE dbo.[client_mail_routes] ADD CONSTRAINT [client_mail_routes_address_check] CHECK ((([address] = lower(TRIM([address]))) AND ([address] LIKE '%@%')));
GO
ALTER TABLE dbo.[client_mail_routes] ADD CONSTRAINT [client_mail_routes_client_id_fkey] FOREIGN KEY ([client_id]) REFERENCES dbo.[clients]([id]);
GO
