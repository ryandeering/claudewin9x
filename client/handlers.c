/*
 * handlers.c - Tool approval, file operation, and command handlers
 */

#include <conio.h>
#include "handlers.h"
#include "http.h"
#include "util.h"

typedef struct {
    char id[64];
    char *result;
} CacheEntry;

static CacheEntry fs_cache[IDEMPOTENCY_CACHE_SIZE];
static int fs_cache_index = 0;

static CacheEntry cmd_cache[IDEMPOTENCY_CACHE_SIZE];
static int cmd_cache_index = 0;

static const char *cache_lookup(CacheEntry *cache, const char *id)
{
    int i;
    for (i = 0; i < IDEMPOTENCY_CACHE_SIZE; i++) {
        if (cache[i].id[0] && strcmp(cache[i].id, id) == 0) {
            return cache[i].result;
        }
    }
    return NULL;
}

static void cache_store(CacheEntry *cache, int *index, const char *id,
                        const char *result)
{
    int slot = *index;

    if (cache[slot].result) {
        free(cache[slot].result);
        cache[slot].result = NULL;
    }

    strncpy(cache[slot].id, id, sizeof(cache[slot].id) - 1);
    cache[slot].id[sizeof(cache[slot].id) - 1] = '\0';
    cache[slot].result = strdup(result);

    *index = (slot + 1) % IDEMPOTENCY_CACHE_SIZE;
}

int poll_approval(void)
{
    static char response[BUFFER_SIZE];
    char path[256];
    char local_session_id[64];
    cJSON *json;
    const cJSON *has_pending;
    const cJSON *approval_id;
    const cJSON *tool_name;
    const cJSON *tool_input;

    EnterCriticalSection(&g_state.output_lock);
    strncpy(local_session_id, g_state.session_id, sizeof(local_session_id));
    local_session_id[sizeof(local_session_id) - 1] = '\0';

    if (g_state.has_pending_approval || g_state.approval_in_progress) {
        LeaveCriticalSection(&g_state.output_lock);
        return 0;
    }
    LeaveCriticalSection(&g_state.output_lock);

    if (!local_session_id[0]) {
        return 0;
    }

    snprintf(path, sizeof(path), "/approval/poll?session_id=%s",
             local_session_id);

    if (http_request("GET", path, NULL, response, sizeof(response)) !=
        HTTP_OK) {
        Sleep(POLL_BACKOFF_MS);
        return 0;
    }

    json = cJSON_Parse(response);
    if (!json) {
        return 0;
    }

    has_pending = cJSON_GetObjectItem(json, "has_pending");
    if (!cJSON_IsTrue(has_pending)) {
        cJSON_Delete(json);
        return 0;
    }

    approval_id = cJSON_GetObjectItem(json, "approval_id");
    tool_name = cJSON_GetObjectItem(json, "tool_name");
    tool_input = cJSON_GetObjectItem(json, "tool_input");

    EnterCriticalSection(&g_state.output_lock);

    if (cJSON_IsString(approval_id)) {
        strncpy(g_state.approval_id, approval_id->valuestring,
                sizeof(g_state.approval_id) - 1);
        g_state.approval_id[sizeof(g_state.approval_id) - 1] = '\0';
    } else {
        g_state.approval_id[0] = '\0';
    }

    if (cJSON_IsString(tool_name)) {
        strncpy(g_state.approval_tool_name, tool_name->valuestring,
                sizeof(g_state.approval_tool_name) - 1);
        g_state.approval_tool_name[sizeof(g_state.approval_tool_name) - 1] =
            '\0';
    } else {
        strcpy(g_state.approval_tool_name, "unknown");
    }

    if (cJSON_IsString(tool_input) && tool_input->valuestring[0]) {
        strncpy(g_state.approval_tool_input, tool_input->valuestring,
                sizeof(g_state.approval_tool_input) - 1);
        g_state.approval_tool_input[sizeof(g_state.approval_tool_input) - 1] =
            '\0';
    } else {
        g_state.approval_tool_input[0] = '\0';
    }

    g_state.has_pending_approval = 1;

    LeaveCriticalSection(&g_state.output_lock);

    cJSON_Delete(json);
    return 1;
}

int process_approval(void)
{
    static char response[BUFFER_SIZE];
    static char local_tool_input[BUFFER_SIZE];
    char body[512];
    char local_approval_id[64];
    char local_tool_name[128];
    int key;
    int approved;

    EnterCriticalSection(&g_state.output_lock);
    if (!g_state.has_pending_approval) {
        LeaveCriticalSection(&g_state.output_lock);
        return 0;
    }

    strncpy(local_approval_id, g_state.approval_id, sizeof(local_approval_id));
    local_approval_id[sizeof(local_approval_id) - 1] = '\0';
    strncpy(local_tool_name, g_state.approval_tool_name,
            sizeof(local_tool_name));
    local_tool_name[sizeof(local_tool_name) - 1] = '\0';
    strncpy(local_tool_input, g_state.approval_tool_input,
            sizeof(local_tool_input));
    local_tool_input[sizeof(local_tool_input) - 1] = '\0';

    g_state.approval_in_progress = 1;
    g_state.has_pending_approval = 0;
    LeaveCriticalSection(&g_state.output_lock);

    if (g_state.skip_permissions) {
        printf("[Auto-approving: %s]\n", local_tool_name);
        approved = 1;
    } else {
        printf("\n");
        printf("========================================\n");
        printf("  TOOL APPROVAL REQUIRED\n");
        printf("========================================\n");
        printf("Tool: %s\n", local_tool_name);

        if (local_tool_input[0]) {
            printf("Input: %s\n", local_tool_input);
        }

        printf("----------------------------------------\n");
        printf("Allow this tool? (Y/N): ");
        fflush(stdout);

        key = getch();
        printf("%c\n", key);

        approved = (key == 'y' || key == 'Y') ? 1 : 0;
    }

    if (local_approval_id[0]) {
        snprintf(body, sizeof(body), "{\"approval_id\":\"%s\",\"approved\":%s}",
                 local_approval_id, approved ? "true" : "false");

        if (http_request("POST", "/approval/respond", body, response,
                         sizeof(response)) == HTTP_OK) {
            printf("[%s]\n", approved ? "Approved" : "Rejected");
        }
    }

    printf("========================================\n\n");

    EnterCriticalSection(&g_state.output_lock);
    g_state.approval_in_progress = 0;
    LeaveCriticalSection(&g_state.output_lock);

    return 1;
}

int handle_approval(void)
{
    static char response[BUFFER_SIZE];
    char path[256];
    char body[512];
    cJSON *json;
    const cJSON *has_pending;
    const cJSON *approval_id;
    const cJSON *tool_name;
    const cJSON *tool_input;
    int key;

    if (!g_state.session_id[0]) {
        return 0;
    }

    snprintf(path, sizeof(path), "/approval/poll?session_id=%s",
             g_state.session_id);

    if (http_request("GET", path, NULL, response, sizeof(response)) !=
        HTTP_OK) {
        Sleep(POLL_BACKOFF_MS);
        return 0;
    }

    json = cJSON_Parse(response);
    if (!json) {
        return 0;
    }

    has_pending = cJSON_GetObjectItem(json, "has_pending");
    if (!cJSON_IsTrue(has_pending)) {
        cJSON_Delete(json);
        return 0;
    }

    approval_id = cJSON_GetObjectItem(json, "approval_id");
    tool_name = cJSON_GetObjectItem(json, "tool_name");
    tool_input = cJSON_GetObjectItem(json, "tool_input");

    printf("\n");
    printf("========================================\n");
    printf("  TOOL APPROVAL REQUIRED\n");
    printf("========================================\n");
    printf("Tool: %s\n",
           cJSON_IsString(tool_name) ? tool_name->valuestring : "unknown");

    if (cJSON_IsString(tool_input) && tool_input->valuestring[0]) {
        printf("Input: %s\n", tool_input->valuestring);
    }

    printf("----------------------------------------\n");
    printf("Allow this tool? (Y/N): ");
    fflush(stdout);

    key = getch();
    printf("%c\n", key);

    if (cJSON_IsString(approval_id)) {
        int approved = (key == 'y' || key == 'Y') ? 1 : 0;

        snprintf(body, sizeof(body), "{\"approval_id\":\"%s\",\"approved\":%s}",
                 approval_id->valuestring, approved ? "true" : "false");

        if (http_request("POST", "/approval/respond", body, response,
                         sizeof(response)) == HTTP_OK) {
            printf("[%s]\n", approved ? "Approved" : "Rejected");
        }
    }

    printf("========================================\n\n");

    cJSON_Delete(json);
    return 1;
}

static void handle_list_op(const char *full_path, cJSON *result)
{
    char search_path[MAX_PATH_LEN + 8];
    WIN32_FIND_DATA find_data;
    HANDLE hfind;
    cJSON *entries;
    size_t len = strlen(full_path);

    if (len > 0 && full_path[len - 1] == '\\') {
        snprintf(search_path, sizeof(search_path), "%s*.*", full_path);
    } else {
        snprintf(search_path, sizeof(search_path), "%s\\*.*", full_path);
    }
    entries = cJSON_CreateArray();

    hfind = FindFirstFile(search_path, &find_data);
    if (hfind == INVALID_HANDLE_VALUE) {
        cJSON_AddStringToObject(result, "error", "Directory not found");
        cJSON_Delete(entries);
        return;
    }

    do {
        cJSON *entry;

        if (strcmp(find_data.cFileName, ".") == 0 ||
            strcmp(find_data.cFileName, "..") == 0) {
            continue;
        }

        entry = cJSON_CreateObject();
        cJSON_AddStringToObject(entry, "name", find_data.cFileName);

        if (find_data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
            cJSON_AddStringToObject(entry, "type", "dir");
            cJSON_AddNumberToObject(entry, "size", 0);
        } else {
            cJSON_AddStringToObject(entry, "type", "file");
            cJSON_AddNumberToObject(entry, "size", find_data.nFileSizeLow);
        }

        cJSON_AddItemToArray(entries, entry);
    } while (FindNextFile(hfind, &find_data));

    FindClose(hfind);
    cJSON_AddItemToObject(result, "entries", entries);
}

static void handle_read_op(const char *full_path, cJSON *result)
{
    FILE *fp;
    char *file_buffer;
    int bytes_read;

    file_buffer = malloc(BUFFER_SIZE * 2);
    if (!file_buffer) {
        cJSON_AddStringToObject(result, "error", "Out of memory");
        return;
    }

    fp = fopen(full_path, "rb");
    if (!fp) {
        cJSON_AddStringToObject(result, "error", "File not found");
        free(file_buffer);
        return;
    }

    bytes_read = fread(file_buffer, 1, BUFFER_SIZE * 2 - 1, fp);
    fclose(fp);

    file_buffer[bytes_read] = '\0';
    cJSON_AddStringToObject(result, "content", file_buffer);
    free(file_buffer);
}

static void handle_write_op(const char *full_path, const char *content,
                            cJSON *result)
{
    FILE *fp;

    if (!content) {
        cJSON_AddStringToObject(result, "error", "No content provided");
        return;
    }

    fp = fopen(full_path, "wb");
    if (!fp) {
        cJSON_AddStringToObject(result, "error", "Could not create file");
        return;
    }

    {
        size_t content_len = strlen(content);
        size_t written = fwrite(content, 1, content_len, fp);
        fclose(fp);
        if (written != content_len) {
            cJSON_AddStringToObject(result, "error", "Write failed");
            return;
        }
    }
}

static void handle_mkdir_op(const char *full_path, cJSON *result)
{
    if (!CreateDirectory(full_path, NULL)) {
        DWORD err = GetLastError();
        if (err != ERROR_ALREADY_EXISTS) {
            cJSON_AddStringToObject(result, "error",
                                    "Could not create directory");
        }
    }
}

int handle_fileop(void)
{
    static char response[BUFFER_SIZE];
    char full_path[MAX_PATH_LEN];
    cJSON *json;
    const cJSON *has_pending;
    const cJSON *op_id;
    const cJSON *operation;
    const cJSON *filepath;
    const cJSON *content;
    cJSON *result;
    char *result_str;
    const char *op;
    const char *cached_result;

    if (http_request("GET", "/fs/poll", NULL, response, sizeof(response)) !=
        HTTP_OK) {
        Sleep(POLL_BACKOFF_MS);
        return 0;
    }

    json = cJSON_Parse(response);
    if (!json) {
        return 0;
    }

    has_pending = cJSON_GetObjectItem(json, "has_pending");
    if (!cJSON_IsTrue(has_pending)) {
        cJSON_Delete(json);
        return 0;
    }

    op_id = cJSON_GetObjectItem(json, "op_id");
    operation = cJSON_GetObjectItem(json, "operation");
    filepath = cJSON_GetObjectItem(json, "path");
    content = cJSON_GetObjectItem(json, "content");

    if (!cJSON_IsString(op_id) || !cJSON_IsString(operation) ||
        !cJSON_IsString(filepath)) {
        log_error("handle_fileop", "malformed file operation request");
        cJSON_Delete(json);
        return 0;
    }

    cached_result = cache_lookup(fs_cache, op_id->valuestring);
    if (cached_result) {
        printf("[FS: replaying cached result for %s]\n", op_id->valuestring);
        HttpResult cached_ret = http_request(
            "POST", "/fs/result", cached_result, response, sizeof(response));
        if (cached_ret != HTTP_OK) {
            log_error("handle_fileop", http_error_string(cached_ret));
        }
        cJSON_Delete(json);
        return 1;
    }

    op = operation->valuestring;
    printf("[FS: %s %s]\n", op, filepath->valuestring);

    if (build_full_path(filepath->valuestring, full_path, sizeof(full_path)) <
        0) {
        log_error("handle_fileop", "path too long or traversal rejected");
        cJSON_Delete(json);
        return 0;
    }

    result = cJSON_CreateObject();
    cJSON_AddStringToObject(result, "op_id", op_id->valuestring);

    if (strcmp(op, "list") == 0) {
        handle_list_op(full_path, result);
    } else if (strcmp(op, "read") == 0) {
        handle_read_op(full_path, result);
    } else if (strcmp(op, "write") == 0) {
        const char *content_str =
            cJSON_IsString(content) ? content->valuestring : NULL;
        handle_write_op(full_path, content_str, result);
    } else if (strcmp(op, "mkdir") == 0) {
        handle_mkdir_op(full_path, result);
    } else {
        cJSON_AddStringToObject(result, "error", "Unknown operation");
    }

    result_str = cJSON_PrintUnformatted(result);
    if (result_str) {
        cache_store(fs_cache, &fs_cache_index, op_id->valuestring, result_str);
        HttpResult post_ret = http_request("POST", "/fs/result", result_str,
                                           response, sizeof(response));
        if (post_ret != HTTP_OK) {
            log_error("handle_fileop", http_error_string(post_ret));
        }
        free(result_str);
    }

    cJSON_Delete(result);
    cJSON_Delete(json);
    return 1;
}

/*
 * Execute command on Windows 2000/XP/beyond (NT-based).
 * Uses cmd.exe with stderr redirection.
 */
static int execute_command_nt(const char *command, char *output,
                              size_t output_size)
{
    char cmdline[1024];
    FILE *pipe;
    int output_len = 0;
    int ch;
    int exit_code;
    const size_t overhead = sizeof("cmd.exe /c ") - 1 + sizeof(" 2>&1");
    const size_t max_cmd_len = sizeof(cmdline) - overhead;

    if (strlen(command) > max_cmd_len) {
        strncpy(output, "Command too long", output_size - 1);
        output[output_size - 1] = '\0';
        return -1;
    }

    snprintf(cmdline, sizeof(cmdline), "cmd.exe /c %s 2>&1", command);
    pipe = _popen(cmdline, "r");
    if (!pipe) {
        strncpy(output, "Failed to execute command", output_size - 1);
        output[output_size - 1] = '\0';
        return -1;
    }

    while ((size_t)output_len < output_size - 1 && (ch = fgetc(pipe)) != EOF) {
        output[output_len++] = (char)ch;
    }
    output[output_len] = '\0';

    exit_code = _pclose(pipe);
    return exit_code;
}

/*
 * Execute command on Windows 95/98/ME
 * Uses temp file redirection since popen and stdout on Windows 9x is crap and limited..
 */
static int execute_command_9x(const char *command, char *output,
                              size_t output_size)
{
    char cmdline[2048];
    char temp_file[MAX_PATH];
    const char *temp_dir;
    FILE *fp;
    int exit_code;
    size_t max_cmd_len;

    temp_dir = getenv("TEMP");
    if (!temp_dir) {
        temp_dir = getenv("TMP");
    }
    if (!temp_dir) {
        temp_dir = "C:";
    }

    snprintf(temp_file, sizeof(temp_file), "%s\\CMDOUT.TMP", temp_dir);

    max_cmd_len = sizeof(cmdline) - 15 - 3 - strlen(temp_file) - 1;

    if (strlen(command) > max_cmd_len) {
        strncpy(output, "Command too long", output_size - 1);
        output[output_size - 1] = '\0';
        return -1;
    }

    snprintf(cmdline, sizeof(cmdline), "command.com /c %s > %s", command,
             temp_file);

    printf("[Exec: %s]\n", cmdline);
    exit_code = system(cmdline);
    printf("[Exit: %d]\n", exit_code);

    fp = fopen(temp_file, "r");
    if (fp) {
        int output_len = 0;
        int ch;
        while ((size_t)output_len < output_size - 1 &&
               (ch = fgetc(fp)) != EOF) {
            output[output_len++] = (char)ch;
        }
        output[output_len] = '\0';
        fclose(fp);
        remove(temp_file);
        printf("[Read %d chars from temp]\n", output_len);
    } else {
        log_error("exec_9x", "Could not open temp file");
        strncpy(output, "Error: Could not capture output", output_size - 1);
        output[output_size - 1] = '\0';
    }

    return exit_code;
}

int handle_command(void)
{
    static char response[BUFFER_SIZE];
    char *cmd_output;
    char old_workdir[MAX_PATH_LEN];
    cJSON *json;
    const cJSON *has_pending;
    const cJSON *cmd_id;
    cJSON *command;
    const cJSON *workdir;
    cJSON *result;
    char *result_str;
    const char *cached_result;
    int exit_code = 0;
    int changed_dir = 0;
    DWORD ver;
    DWORD major;

    if (http_request("GET", "/cmd/poll", NULL, response, sizeof(response)) !=
        HTTP_OK) {
        Sleep(POLL_BACKOFF_MS);
        return 0;
    }

    json = cJSON_Parse(response);
    if (!json) {
        return 0;
    }

    has_pending = cJSON_GetObjectItem(json, "has_pending");
    if (!cJSON_IsTrue(has_pending)) {
        cJSON_Delete(json);
        return 0;
    }

    cmd_id = cJSON_GetObjectItem(json, "cmd_id");
    command = cJSON_GetObjectItem(json, "command");
    workdir = cJSON_GetObjectItem(json, "working_directory");

    if (!cJSON_IsString(cmd_id) || !cJSON_IsString(command)) {
        log_error("handle_command", "malformed command request");
        cJSON_Delete(json);
        return 0;
    }

    cached_result = cache_lookup(cmd_cache, cmd_id->valuestring);
    if (cached_result) {
        printf("[CMD: replaying cached result for %s]\n", cmd_id->valuestring);
        HttpResult cached_ret = http_request(
            "POST", "/cmd/result", cached_result, response, sizeof(response));
        if (cached_ret != HTTP_OK) {
            log_error("handle_command", http_error_string(cached_ret));
        }
        cJSON_Delete(json);
        return 1;
    }

    if (cJSON_IsString(workdir) && workdir->valuestring[0]) {
        char full_workdir[MAX_PATH_LEN];
        if (build_full_path(workdir->valuestring, full_workdir,
                            sizeof(full_workdir)) == 0) {
            GetCurrentDirectory(sizeof(old_workdir), old_workdir);
            if (SetCurrentDirectory(full_workdir)) {
                changed_dir = 1;
                printf("[CD: %s]\n", full_workdir);
            } else {
                log_error("command", "Could not change directory");
            }
        }
    }

    {
        char cmd_copy[1024];
        strncpy(cmd_copy, command->valuestring, sizeof(cmd_copy) - 1);
        cmd_copy[sizeof(cmd_copy) - 1] = '\0';
        path_to_backslashes(cmd_copy);
        printf("[CMD: %s]\n", cmd_copy);

        cmd_output = malloc(MAX_CMD_OUTPUT);
        if (!cmd_output) {
            log_error("handle_command", "out of memory for command output");
            cJSON_Delete(json);
            if (changed_dir) {
                SetCurrentDirectory(old_workdir);
            }
            return 0;
        }
        cmd_output[0] = '\0';

        ver = GetVersion();
        major = (DWORD)(LOBYTE(LOWORD(ver)));

        if (major >= 5) {
            exit_code =
                execute_command_nt(cmd_copy, cmd_output, MAX_CMD_OUTPUT);
        } else {
            exit_code =
                execute_command_9x(cmd_copy, cmd_output, MAX_CMD_OUTPUT);
        }

        if (cmd_output[0] != '\0') {
            printf("%s", cmd_output);
            {
                size_t len = strlen(cmd_output);
                if (len > 0 && cmd_output[len - 1] != '\n') {
                    printf("\n");
                }
            }
        }
    }

    if (changed_dir) {
        SetCurrentDirectory(old_workdir);
    }

    result = cJSON_CreateObject();
    cJSON_AddStringToObject(result, "command_id", cmd_id->valuestring);
    cJSON_AddStringToObject(result, "stdout", cmd_output);
    cJSON_AddStringToObject(result, "stderr", "");
    cJSON_AddNumberToObject(result, "exit_code", exit_code);

    result_str = cJSON_PrintUnformatted(result);
    if (result_str) {
        cache_store(cmd_cache, &cmd_cache_index, cmd_id->valuestring,
                    result_str);
        HttpResult post_ret = http_request("POST", "/cmd/result", result_str,
                                           response, sizeof(response));
        if (post_ret != HTTP_OK) {
            log_error("handle_command", http_error_string(post_ret));
        }
        free(result_str);
    }

    cJSON_Delete(result);
    cJSON_Delete(json);
    free(cmd_output);
    return 1;
}
