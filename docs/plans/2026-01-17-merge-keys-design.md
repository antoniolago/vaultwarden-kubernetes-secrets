# Merge Keys Instead of Clearing for Existing Secrets

## Problem

When updating an existing Kubernetes Secret, the current `UpdateSecretAsync` method replaces all keys with only the Vaultwarden-synced keys. This causes:

1. **Mixed ownership loss** - Keys added by other systems or manual edits are wiped out
2. **Multi-item conflicts** - When multiple Vaultwarden items target the same secret (via `secret-name`), syncing one item removes keys from other items

## Solution

Merge keys instead of replacing, using an annotation to track which keys are Vaultwarden-managed.

### New Annotation

```
vaultwarden-kubernetes-secrets/managed-keys: ["username","password","api-key"]
```

A JSON array of key names that the sync service manages for this secret.

### Update Flow

1. Read existing secret from Kubernetes
2. Get previous managed keys from annotation (or empty list if not present)
3. Start with existing secret's data
4. Remove keys that were in previous managed-keys list
5. Add/overwrite with new Vaultwarden-synced keys
6. Update managed-keys annotation with new key names
7. Replace secret with merged data

### Example

**Before sync** - existing secret:
- Data: `{ "db-host": "external", "username": "old", "password": "old" }`
- Annotation: `managed-keys: ["username", "password"]`

**Vaultwarden provides**: `{ "username": "new-user", "api-key": "xyz" }`

**After sync**:
- Data: `{ "db-host": "external", "username": "new-user", "api-key": "xyz" }`
- Annotation: `managed-keys: ["username", "api-key"]`

Result: `password` removed (was managed, no longer synced), `db-host` preserved (external), `username` updated, `api-key` added.

## Implementation

### Files to Modify

1. **Constants.cs** - Add annotation key constant:
   ```csharp
   public const string ManagedKeysAnnotationKey = "vaultwarden-kubernetes-secrets/managed-keys";
   ```

2. **KubernetesService.cs** - Modify `UpdateSecretAsync`:
   - Fetch existing secret before updating
   - Add helper methods to parse/serialize managed keys JSON
   - Implement merge logic

3. **IKubernetesService.cs** - No signature changes needed

### Edge Cases

| Case | Behavior |
|------|----------|
| Secret exists but has no `managed-keys` annotation | Treat as empty list (first sync to existing secret) - only add new keys, don't remove any |
| Secret doesn't exist | Fall through to create (existing behavior) |
| Malformed JSON in annotation | Log warning, treat as empty list, overwrite with valid annotation |

## Testing

### Unit Tests

1. **Merge logic**:
   - Merges new keys with existing external keys
   - Removes previously managed keys no longer synced
   - Preserves external keys not in managed-keys list
   - Handles empty managed-keys annotation
   - Handles malformed JSON gracefully

2. **Annotation handling**:
   - Parses managed-keys JSON array correctly
   - Serializes new managed-keys list correctly
   - Handles missing annotation

### Integration Tests

- End-to-end sync preserves external keys
- Multiple Vaultwarden items targeting same secret work correctly

## Backwards Compatibility

No breaking changes. Existing secrets without the `managed-keys` annotation will work - treated as first sync where no keys are removed.
