-- Migration: Add sync configuration columns to SyncLogs table
-- Date: 2025-10-18
-- Description: Adds SyncIntervalSeconds and ContinuousSync columns to track actual sync configuration

-- Check if columns already exist before adding them
-- SQLite doesn't support ALTER TABLE ADD COLUMN IF NOT EXISTS directly,
-- so we use a safer approach

-- Add SyncIntervalSeconds column if it doesn't exist
ALTER TABLE SyncLogs ADD COLUMN SyncIntervalSeconds INTEGER NOT NULL DEFAULT 0;

-- Add ContinuousSync column if it doesn't exist  
ALTER TABLE SyncLogs ADD COLUMN ContinuousSync INTEGER NOT NULL DEFAULT 0;
