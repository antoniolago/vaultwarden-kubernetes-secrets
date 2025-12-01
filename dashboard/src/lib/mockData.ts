// Mock data for GitHub Pages demo
// This simulates a production-ready deployment

export const mockOverview = {
  totalSyncs: 1247,
  successfulSyncs: 1198,
  failedSyncs: 49,
  activeSecrets: 24,
  totalNamespaces: 12,
  lastSyncTime: new Date().toISOString(),
  averageSyncDuration: 12.5,
  successRate: 96.07,
  secretsByNamespace: [
    { Namespace: "production", Count: 8 },
    { Namespace: "staging", Count: 6 },
    { Namespace: "development", Count: 4 },
    { Namespace: "monitoring", Count: 3 },
    { Namespace: "logging", Count: 2 },
    { Namespace: "ingress", Count: 1 }
  ],
  recentActivity: [
    {
      id: 1247,
      startTime: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
      status: "Success",
      createdSecrets: 0,
      updatedSecrets: 2,
      failedSecrets: 0,
      durationSeconds: 11.2
    },
    {
      id: 1246,
      startTime: new Date(Date.now() - 45 * 60 * 1000).toISOString(),
      status: "Success",
      createdSecrets: 1,
      updatedSecrets: 0,
      failedSecrets: 0,
      durationSeconds: 13.8
    },
    {
      id: 1245,
      startTime: new Date(Date.now() - 85 * 60 * 1000).toISOString(),
      status: "Success",
      createdSecrets: 0,
      updatedSecrets: 1,
      failedSecrets: 0,
      durationSeconds: 10.5
    },
    {
      id: 1244,
      startTime: new Date(Date.now() - 125 * 60 * 1000).toISOString(),
      status: "Failed",
      createdSecrets: 0,
      updatedSecrets: 0,
      failedSecrets: 1,
      durationSeconds: 8.3
    },
    {
      id: 1243,
      startTime: new Date(Date.now() - 165 * 60 * 1000).toISOString(),
      status: "Success",
      createdSecrets: 0,
      updatedSecrets: 0,
      failedSecrets: 0,
      durationSeconds: 9.7
    }
  ]
};

export const mockNamespaces = [
  {
    namespace: "production",
    secretCount: 8,
    activeSecrets: 8,
    failedSecrets: 0,
    totalDataKeys: 24,
    lastSyncTime: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
    successRate: 100.0
  },
  {
    namespace: "staging",
    secretCount: 6,
    activeSecrets: 6,
    failedSecrets: 0,
    totalDataKeys: 18,
    lastSyncTime: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
    successRate: 100.0
  },
  {
    namespace: "development",
    secretCount: 4,
    activeSecrets: 4,
    failedSecrets: 0,
    totalDataKeys: 12,
    lastSyncTime: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
    successRate: 100.0
  },
  {
    namespace: "monitoring",
    secretCount: 3,
    activeSecrets: 3,
    failedSecrets: 0,
    totalDataKeys: 9,
    lastSyncTime: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
    successRate: 100.0
  },
  {
    namespace: "logging",
    secretCount: 2,
    activeSecrets: 2,
    failedSecrets: 0,
    totalDataKeys: 6,
    lastSyncTime: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
    successRate: 100.0
  },
  {
    namespace: "ingress",
    secretCount: 1,
    activeSecrets: 0,
    failedSecrets: 1,
    totalDataKeys: 0,
    lastSyncTime: new Date(Date.now() - 125 * 60 * 1000).toISOString(),
    successRate: 0.0
  }
];

export const mockSecrets = {
  production: [
    {
      secretName: "database-credentials",
      namespace: "production",
      vaultwardenItemId: "1a2b3c4d",
      vaultwardenItemName: "Production Database",
      lastSynced: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
      status: "Active",
      dataKeysCount: 4,
      lastError: null
    },
    {
      secretName: "api-keys",
      namespace: "production",
      vaultwardenItemId: "2b3c4d5e",
      vaultwardenItemName: "Production API Keys",
      lastSynced: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
      status: "Active",
      dataKeysCount: 3,
      lastError: null
    },
    {
      secretName: "tls-certificates",
      namespace: "production",
      vaultwardenItemId: "3c4d5e6f",
      vaultwardenItemName: "Production TLS",
      lastSynced: new Date(Date.now() - 45 * 60 * 1000).toISOString(),
      status: "Active",
      dataKeysCount: 2,
      lastError: null
    }
  ],
  staging: [
    {
      secretName: "database-credentials",
      namespace: "staging",
      vaultwardenItemId: "4d5e6f7g",
      vaultwardenItemName: "Staging Database",
      lastSynced: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
      status: "Active",
      dataKeysCount: 4,
      lastError: null
    }
  ],
  ingress: [
    {
      secretName: "ingress-tls",
      namespace: "ingress",
      vaultwardenItemId: "5e6f7g8h",
      vaultwardenItemName: "Ingress TLS Certificate",
      lastSynced: new Date(Date.now() - 125 * 60 * 1000).toISOString(),
      status: "Failed",
      dataKeysCount: 0,
      lastError: "Namespace 'ingress' does not exist in Kubernetes cluster"
    }
  ]
};

export const mockSyncLogs = [
  {
    id: 1247,
    startTime: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
    endTime: new Date(Date.now() - 5 * 60 * 1000 + 11200).toISOString(),
    status: "Success",
    processedItems: 24,
    createdSecrets: 0,
    updatedSecrets: 2,
    failedSecrets: 0,
    skippedSecrets: 22,
    deletedSecrets: 0,
    durationSeconds: 11.2,
    errorMessage: null,
    syncType: "Scheduled",
    continuousSync: true,
    syncIntervalSeconds: 600
  },
  {
    id: 1246,
    startTime: new Date(Date.now() - 45 * 60 * 1000).toISOString(),
    endTime: new Date(Date.now() - 45 * 60 * 1000 + 13800).toISOString(),
    status: "Success",
    processedItems: 24,
    createdSecrets: 1,
    updatedSecrets: 0,
    failedSecrets: 0,
    skippedSecrets: 23,
    deletedSecrets: 0,
    durationSeconds: 13.8,
    errorMessage: null,
    syncType: "Scheduled",
    continuousSync: true,
    syncIntervalSeconds: 600
  },
  {
    id: 1245,
    startTime: new Date(Date.now() - 85 * 60 * 1000).toISOString(),
    endTime: new Date(Date.now() - 85 * 60 * 1000 + 10500).toISOString(),
    status: "Success",
    processedItems: 24,
    createdSecrets: 0,
    updatedSecrets: 1,
    failedSecrets: 0,
    skippedSecrets: 23,
    deletedSecrets: 0,
    durationSeconds: 10.5,
    errorMessage: null,
    syncType: "Scheduled",
    continuousSync: true,
    syncIntervalSeconds: 600
  },
  {
    id: 1244,
    startTime: new Date(Date.now() - 125 * 60 * 1000).toISOString(),
    endTime: new Date(Date.now() - 125 * 60 * 1000 + 8300).toISOString(),
    status: "Failed",
    processedItems: 24,
    createdSecrets: 0,
    updatedSecrets: 0,
    failedSecrets: 1,
    skippedSecrets: 23,
    deletedSecrets: 0,
    durationSeconds: 8.3,
    errorMessage: "Failed to sync secret in namespace ingress",
    syncType: "Scheduled",
    continuousSync: true,
    syncIntervalSeconds: 600
  }
];

export const mockDiscovery = {
  totalItems: 32,
  syncedItems: 24,
  unsyncedItems: 8,
  syncCoverage: 75.0,
  statistics: {
    byOrganization: [
      { name: "Infrastructure", total: 16, synced: 14 },
      { name: "Applications", total: 12, synced: 8 },
      { name: "Security", total: 4, synced: 2 }
    ],
    byCollection: [
      { name: "Production Secrets", total: 12, synced: 12 },
      { name: "Staging Secrets", total: 8, synced: 8 },
      { name: "Development Secrets", total: 6, synced: 4 },
      { name: "Other", total: 6, synced: 0 }
    ]
  },
  unsyncedDetails: [
    {
      name: "Legacy Database Password",
      id: "abc123",
      organizationName: "Applications",
      collectionNames: ["Other"],
      reason: "Missing 'namespaces' custom field"
    },
    {
      name: "Old API Key",
      id: "def456",
      organizationName: "Applications",
      collectionNames: ["Other"],
      reason: "Missing 'namespaces' custom field"
    }
  ]
};

export const mockSyncConfig = {
  vaultwarden: {
    serverUrl: "https://vault.example.com",
    organizationId: "org-123",
    organizationName: "Infrastructure Team",
    collectionId: null,
    collectionName: null,
    folderId: null,
    folderName: null
  },
  fieldNames: {
    namespaces: "namespaces",
    secretName: "secret-name"
  },
  sync: {
    syncIntervalSeconds: 600,
    continuousSync: true,
    dryRun: false,
    deleteOrphans: true
  },
  filters: {
    hasOrganizationFilter: true,
    hasCollectionFilter: false,
    hasFolderFilter: false,
    organizationId: "org-123",
    organizationName: "Infrastructure Team",
    collectionId: null,
    collectionName: null,
    folderId: null,
    folderName: null
  }
};

export const mockSyncStatus = {
  syncIntervalSeconds: 600,
  continuousSync: true,
  lastSyncTime: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
  nextSyncTime: null
};
