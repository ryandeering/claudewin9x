/*
 * Claude Code client primarily for win9x/nt - entrypoint
 */

#ifndef CLAUDE_H
#define CLAUDE_H

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <winsock2.h>
#include <windows.h>
#include "third_party/cJSON.h"

#define BUFFER_SIZE 32768
#define MAX_INPUT 1024
#define MAX_PATH_LEN 512
#define MAX_CMD_OUTPUT (BUFFER_SIZE * 4)

#define PORT_API 5000
#define PORT_FILE_DOWNLOAD 5001
#define PORT_FILE_UPLOAD 5002

#define API_KEY "a3f8b2d1-7c4e-4a9f-b6e5-2d8c1f0e3a7b"

#define HEARTBEAT_INTERVAL_MS 30000
#define HTTP_TIMEOUT_SEC 10
#define TRANSFER_TIMEOUT_SEC 30
#define POLL_SLEEP_MS 1000
#define POLL_BACKOFF_MS 2000
#define INPUT_SLEEP_MS 100
#define POLL_INTERVAL_CYCLES 5
#define POLL_TIMEOUT_CYCLES 120
#define IDEMPOTENCY_CACHE_SIZE 16
typedef enum {
    HTTP_OK = 0,
    HTTP_ERR_SOCKET = -1,
    HTTP_ERR_CONNECT = -2,
    HTTP_ERR_OVERFLOW = -3,
    HTTP_ERR_SEND = -4,
    HTTP_ERR_TIMEOUT = -5,
    HTTP_ERR_NO_BODY = -6,
    HTTP_ERR_SERVER = -7,
    HTTP_ERR_TRUNCATED = -8,
    HTTP_ERR_RESPONSE_TOO_LARGE = -9
} HttpResult;

typedef struct {
    char server_ip[64];
    int server_port;
    char session_id[64];
    int connected;
    int running;
    FILE *logfile;
    char logpath[256];
    DWORD last_heartbeat;
    HANDLE poll_thread;
    CRITICAL_SECTION output_lock;
    char pending_output[BUFFER_SIZE];
    int has_pending_output;
    int session_stopped;
    int has_pending_approval;
    int approval_in_progress;
    char approval_id[64];
    char approval_tool_name[128];
    char approval_tool_input[BUFFER_SIZE];
    int skip_permissions;
} ClientState;

extern ClientState g_state;

#endif /* CLAUDE_H */
