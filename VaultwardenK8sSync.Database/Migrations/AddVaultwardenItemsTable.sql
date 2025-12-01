-- Migration: Add VaultwardenItems table
-- This table caches Vaultwarden items so the API doesn't need to authenticate

CREATE TABLE IF NOT EXISTS VaultwardenItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId TEXT NOT NULL,
    Name TEXT NOT NULL,
    FolderId TEXT NULL,
    OrganizationId TEXT NULL,
    FieldCount INTEGER NOT NULL DEFAULT 0,
    Notes TEXT NULL,
    LastFetched TEXT NOT NULL,
    HasNamespacesField INTEGER NOT NULL DEFAULT 0,
    NamespacesJson TEXT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_VaultwardenItems_ItemId ON VaultwardenItems(ItemId);
CREATE INDEX IF NOT EXISTS IX_VaultwardenItems_LastFetched ON VaultwardenItems(LastFetched);
CREATE INDEX IF NOT EXISTS IX_VaultwardenItems_HasNamespacesField ON VaultwardenItems(HasNamespacesField);
