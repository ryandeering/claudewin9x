/*
 * session.c - Session management
 */

#include <conio.h>
#include "session.h"
#include "http.h"
#include "handlers.h"
#include "util.h"

void session_heartbeat(void)
{
    static char response[BUFFER_SIZE];
    char body[256];
    DWORD now;

    if (!g_state.session_id[0]) {
        return;
    }

    now = GetTickCount();

    if (g_state.last_heartbeat != 0 &&
        (now - g_state.last_heartbeat) < HEARTBEAT_INTERVAL_MS) {
        return;
    }

    snprintf(body, sizeof(body), "{\"session_id\":\"%s\"}", g_state.session_id);

    if (http_request("POST", "/heartbeat", body, response, sizeof(response)) ==
        HTTP_OK) {
        g_state.last_heartbeat = now;
    }
}

static void session_poll_output(void)
{
    static char response[BUFFER_SIZE];
    char path[256];
    cJSON *json;
    const cJSON *output_item;
    const cJSON *status_item;
    int idle_count = 0;
    int ever_got_output = 0;
    int spinner = 0;
    int got_output = 0;
    const char *spinchars = "|/-\\";

    if (!g_state.session_id[0]) {
        return;
    }

    while (g_state.session_id[0]) {
        if (kbhit()) {
            getch();
            printf("\r                              \r");
            printf("[Interrupted]\n");
            break;
        }

        got_output = 0;

        if (g_state.poll_thread != NULL) {
            int session_ended = 0;

            session_heartbeat();

            if (process_approval()) {
                continue;
            }

            EnterCriticalSection(&g_state.output_lock);
            if (g_state.has_pending_output) {
                if (!ever_got_output) {
                    printf("\r                              \r");
                }
                print_output(g_state.pending_output);
                g_state.has_pending_output = 0;
                got_output = 1;

                if (strncmp(g_state.pending_output, "[Session", 8) != 0 &&
                    strncmp(g_state.pending_output, "[Using tool", 11) != 0) {
                    ever_got_output = 1;
                }
                idle_count = 0;
            }

            if (g_state.session_stopped) {
                g_state.session_stopped = 0;
                g_state.session_id[0] = '\0';
                g_state.connected = 0;
                session_ended = 1;
            }
            LeaveCriticalSection(&g_state.output_lock);

            if (session_ended) {
                printf("\n[Session ended]\n");
                break;
            }

        } else {
            session_heartbeat();

            while (handle_fileop()) {
            }

            while (handle_command()) {
            }

            handle_approval();

            snprintf(path, sizeof(path), "/output?session_id=%s",
                     g_state.session_id);

            if (http_request("GET", path, NULL, response, sizeof(response)) ==
                HTTP_OK) {
                json = cJSON_Parse(response);
                if (json) {
                    output_item = cJSON_GetObjectItem(json, "output");
                    status_item = cJSON_GetObjectItem(json, "status");

                    if (cJSON_IsString(output_item) &&
                        output_item->valuestring[0]) {
                        if (!ever_got_output) {
                            printf("\r                              \r");
                        }

                        print_output(output_item->valuestring);
                        got_output = 1;

                        if (strncmp(output_item->valuestring, "[Session", 8) !=
                                0 &&
                            strncmp(output_item->valuestring, "[Using tool",
                                    11) != 0) {
                            ever_got_output = 1;
                        }
                        idle_count = 0;
                    }

                    if (cJSON_IsString(status_item) &&
                        strcmp(status_item->valuestring, "stopped") == 0) {
                        printf("\n[Session ended]\n");
                        g_state.session_id[0] = '\0';
                        g_state.connected = 0;
                        cJSON_Delete(json);
                        break;
                    }

                    cJSON_Delete(json);
                }
            } else {
                Sleep(POLL_BACKOFF_MS);
            }
        }

        if (!ever_got_output) {
            printf("\r[%c] Waiting for Claude...  ", spinchars[spinner % 4]);
            fflush(stdout);
            spinner++;
        }

        if (!got_output) {
            idle_count++;

            if (ever_got_output && idle_count >= 2) {
                break;
            }

            if (idle_count > POLL_TIMEOUT_CYCLES) {
                printf("\r                              \r");
                printf("[Timeout waiting for response]\n");
                break;
            }
        }

        Sleep(POLL_SLEEP_MS);
    }
}

void session_connect(const char *working_dir)
{
    static char response[BUFFER_SIZE];
    cJSON *request;
    cJSON *json;
    const cJSON *session_id_item;
    const cJSON *error_item;
    char *body;
    char win_version[128];

    if (g_state.session_id[0]) {
        printf("[Already connected. Use /disconnect first]\n");
        return;
    }

    get_windows_version(win_version, sizeof(win_version));
    printf("[Client: %s]\n", win_version);
    printf("[Connecting to %s:%d...]\n", g_state.server_ip,
           g_state.server_port);

    request = cJSON_CreateObject();
    if (!request) {
        log_error("session", "Out of memory");
        return;
    }

    if (working_dir && working_dir[0]) {
        cJSON_AddStringToObject(request, "working_directory", working_dir);
    }
    cJSON_AddStringToObject(request, "windows_version", win_version);

    body = cJSON_PrintUnformatted(request);
    cJSON_Delete(request);

    if (!body) {
        log_error("session", "Out of memory");
        return;
    }

    HttpResult ret =
        http_request("POST", "/start", body, response, sizeof(response));
    free(body);

    if (ret != HTTP_OK) {
        log_error("session", http_error_string(ret));
        return;
    }

    json = cJSON_Parse(response);
    if (!json) {
        log_error("session", "Invalid response from server");
        return;
    }

    error_item = cJSON_GetObjectItem(json, "error");
    if (cJSON_IsString(error_item)) {
        log_error("session", error_item->valuestring);
        cJSON_Delete(json);
        return;
    }

    session_id_item = cJSON_GetObjectItem(json, "session_id");
    if (!cJSON_IsString(session_id_item)) {
        log_error("session", "No session ID returned");
        cJSON_Delete(json);
        return;
    }

    if (g_state.poll_thread != NULL) {
        EnterCriticalSection(&g_state.output_lock);
    }
    strncpy(g_state.session_id, session_id_item->valuestring,
            sizeof(g_state.session_id) - 1);
    g_state.session_id[sizeof(g_state.session_id) - 1] = '\0';
    g_state.connected = 1;
    g_state.session_stopped = 0;
    g_state.last_heartbeat = GetTickCount();
    if (g_state.poll_thread != NULL) {
        LeaveCriticalSection(&g_state.output_lock);
    }
    cJSON_Delete(json);

    printf("[Connected! Session: %s]\n", g_state.session_id);
    printf("[Ready - type a message to start chatting]\n\n");
}

void session_disconnect(void)
{
    static char response[BUFFER_SIZE];
    cJSON *request;

    if (!g_state.session_id[0]) {
        printf("[Not connected]\n");
        return;
    }

    request = cJSON_CreateObject();
    if (request) {
        char *body;
        cJSON_AddStringToObject(request, "session_id", g_state.session_id);
        body = cJSON_PrintUnformatted(request);
        cJSON_Delete(request);

        if (body) {
            http_request("POST", "/stop", body, response, sizeof(response));
            free(body);
        }
    }

    if (g_state.poll_thread != NULL) {
        EnterCriticalSection(&g_state.output_lock);
    }
    g_state.session_id[0] = '\0';
    g_state.connected = 0;
    if (g_state.poll_thread != NULL) {
        LeaveCriticalSection(&g_state.output_lock);
    }
    printf("[Disconnected]\n");
}

void session_send_input(const char *text)
{
    static char response[BUFFER_SIZE];
    char text_with_newline[MAX_INPUT + 2];
    cJSON *request;
    cJSON *json;
    char *body;

    if (!g_state.session_id[0]) {
        printf("[Not connected. Use /connect first]\n");
        return;
    }

    log_user_input(text);

    snprintf(text_with_newline, sizeof(text_with_newline), "%s\n", text);

    request = cJSON_CreateObject();
    if (!request) {
        log_error("input", "Out of memory");
        return;
    }

    cJSON_AddStringToObject(request, "session_id", g_state.session_id);
    cJSON_AddStringToObject(request, "text", text_with_newline);

    body = cJSON_PrintUnformatted(request);
    cJSON_Delete(request);

    if (!body) {
        log_error("input", "Out of memory");
        return;
    }

    {
        HttpResult ret =
            http_request("POST", "/input", body, response, sizeof(response));
        free(body);

        if (ret != HTTP_OK) {
            log_error("input", http_error_string(ret));
            return;
        }
    }

    json = cJSON_Parse(response);
    if (json) {
        const cJSON *error_item = cJSON_GetObjectItem(json, "error");
        if (cJSON_IsString(error_item)) {
            log_error("input", error_item->valuestring);
            cJSON_Delete(json);
            return;
        }
        cJSON_Delete(json);
    }

    session_poll_output();
}

void session_poll_once(void)
{
    static char response[BUFFER_SIZE];
    char path[256];
    cJSON *json;

    if (!g_state.session_id[0]) {
        printf("[Not connected]\n");
        return;
    }

    snprintf(path, sizeof(path), "/output?session_id=%s", g_state.session_id);

    if (http_request("GET", path, NULL, response, sizeof(response)) ==
        HTTP_OK) {
        json = cJSON_Parse(response);
        if (json) {
            const cJSON *output_item = cJSON_GetObjectItem(json, "output");
            const cJSON *status_item = cJSON_GetObjectItem(json, "status");

            if (cJSON_IsString(output_item) && output_item->valuestring[0]) {
                print_output(output_item->valuestring);
            } else {
                printf("[No new output]\n");
            }

            if (cJSON_IsString(status_item) &&
                strcmp(status_item->valuestring, "stopped") == 0) {
                printf("\n[Session ended]\n");
                if (g_state.poll_thread != NULL) {
                    EnterCriticalSection(&g_state.output_lock);
                }
                g_state.session_id[0] = '\0';
                g_state.connected = 0;
                if (g_state.poll_thread != NULL) {
                    LeaveCriticalSection(&g_state.output_lock);
                }
            }

            cJSON_Delete(json);
        }
    } else {
        log_error("poll", "Failed to get output");
    }
}
