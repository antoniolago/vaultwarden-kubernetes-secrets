import {
  Modal,
  ModalDialog,
  Typography,
  Box,
  Chip,
  IconButton,
  CircularProgress,
  Sheet,
} from '@mui/joy'

interface KeysModalProps {
  open: boolean
  onClose: () => void
  title: string
  subtitle?: string
  loading: boolean
  items: Array<{ label: string; keys: string[] }>
  emptyMessage?: string
}

export default function KeysModal({
  open,
  onClose,
  title,
  subtitle,
  loading,
  items,
  emptyMessage = 'No data found',
}: KeysModalProps) {
  const totalKeys = items.reduce((sum, item) => sum + item.keys.length, 0)

  return (
    <Modal open={open} onClose={onClose}>
      <ModalDialog
        sx={{
          minWidth: { xs: '90vw', sm: 600, md: 700 },
          maxWidth: '90vw',
          maxHeight: '85vh',
          overflow: 'hidden',
          borderRadius: 'lg',
          boxShadow: 'lg',
        }}
      >
        {/* Header */}
        <Box
          sx={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'flex-start',
            mb: 2,
            pb: 2,
            borderBottom: '1px solid',
            borderColor: 'divider',
          }}
        >
          <Box sx={{ flex: 1 }}>
            <Typography level="h4" sx={{ mb: 0.5, fontWeight: 600 }}>
              {title}
            </Typography>
            {subtitle && (
              <Typography level="body-sm" sx={{ color: 'text.secondary' }}>
                {subtitle}
              </Typography>
            )}
            {!loading && totalKeys > 0 && (
              <Box sx={{ mt: 1 }}>
                <Chip size="sm" variant="soft" color="primary">
                  {totalKeys} {totalKeys === 1 ? 'key' : 'keys'}
                </Chip>
              </Box>
            )}
          </Box>
          <IconButton
            onClick={onClose}
            variant="plain"
            color="neutral"
            sx={{
              ml: 1,
              '&:hover': {
                backgroundColor: 'danger.softBg',
                color: 'danger.solidColor',
              },
            }}
          >
            ✕
          </IconButton>
        </Box>

        {/* Content */}
        <Box sx={{ overflow: 'auto', flex: 1 }}>
          {loading ? (
            <Box
              sx={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                justifyContent: 'center',
                py: 6,
              }}
            >
              <CircularProgress size="lg" />
              <Typography level="body-sm" sx={{ mt: 2, color: 'text.tertiary' }}>
                Loading...
              </Typography>
            </Box>
          ) : items.length > 0 ? (
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              {items.map((item, idx) => (
                <Sheet
                  key={idx}
                  variant="outlined"
                  sx={{
                    p: 2.5,
                    borderRadius: 'md',
                    backgroundColor: 'background.surface',
                    transition: 'all 0.2s',
                    '&:hover': {
                      borderColor: 'primary.outlinedBorder',
                      boxShadow: 'sm',
                    },
                  }}
                >
                  {items.length > 1 && (
                    <Typography
                      level="title-sm"
                      sx={{
                        mb: 1.5,
                        fontWeight: 600,
                        color: 'text.primary',
                      }}
                    >
                      📦 {item.label}
                    </Typography>
                  )}
                  <Box
                    sx={{
                      display: 'flex',
                      flexWrap: 'wrap',
                      gap: 1,
                    }}
                  >
                    {item.keys.map((key, keyIdx) => (
                      <Chip
                        key={keyIdx}
                        variant="soft"
                        color="primary"
                        size="md"
                        sx={{
                          fontFamily: 'monospace',
                          fontSize: '0.875rem',
                          px: 1.5,
                          py: 0.5,
                          '&:hover': {
                            backgroundColor: 'primary.solidBg',
                            color: 'primary.solidColor',
                          },
                        }}
                      >
                        🔑 {key}
                      </Chip>
                    ))}
                  </Box>
                </Sheet>
              ))}
            </Box>
          ) : (
            <Box
              sx={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                justifyContent: 'center',
                py: 6,
              }}
            >
              <Typography
                level="h2"
                sx={{ fontSize: '3rem', mb: 1, opacity: 0.3 }}
              >
                🔍
              </Typography>
              <Typography level="body-sm" sx={{ color: 'text.tertiary' }}>
                {emptyMessage}
              </Typography>
            </Box>
          )}
        </Box>
      </ModalDialog>
    </Modal>
  )
}
