/*
 * Claude Code client primarily for win9x/nt - entrypoint
 */

#include "claude.h"
#include "commands.h"
#include "session.h"
#include "handlers.h"
#include "http.h"
#include "util.h"
#include <conio.h>
#include <process.h>

ClientState g_state = {.server_ip = "192.168.2.1",
                       .server_port = PORT_API,
                       .session_id = "",
                       .connected = 0,
                       .running = 1,
                       .logfile = NULL,
                       .logpath = "claude.log",
                       .last_heartbeat = 0,
                       .poll_thread = NULL,
                       .has_pending_output = 0,
                       .session_stopped = 0,
                       .has_pending_approval = 0,
                       .approval_in_progress = 0,
                       .approval_id = "",
                       .approval_tool_name = "",
                       .approval_tool_input = "",
                       .skip_permissions = 0};

static unsigned __stdcall poll_thread_func(void *param)
{
    static char response[BUFFER_SIZE];
    char path[256];
    char local_session_id[64];
    cJSON *json;

    (void)param;

    while (g_state.running) {
        EnterCriticalSection(&g_state.output_lock);
        strncpy(local_session_id, g_state.session_id, sizeof(local_session_id));
        local_session_id[sizeof(local_session_id) - 1] = '\0';
        LeaveCriticalSection(&g_state.output_lock);

        if (!local_session_id[0]) {
            Sleep(POLL_SLEEP_MS);
            continue;
        }

        while (handle_fileop()) {
        }

        while (handle_command()) {
        }

        poll_approval();

        snprintf(path, sizeof(path), "/output?session_id=%s", local_session_id);

        if (http_request("GET", path, NULL, response, sizeof(response)) ==
            HTTP_OK) {
            json = cJSON_Parse(response);
            if (json) {
                const cJSON *output = cJSON_GetObjectItem(json, "output");
                const cJSON *status = cJSON_GetObjectItem(json, "status");

                EnterCriticalSection(&g_state.output_lock);

                if (cJSON_IsString(output) && output->valuestring[0]) {
                    strncpy(g_state.pending_output, output->valuestring,
                            BUFFER_SIZE - 1);
                    g_state.pending_output[BUFFER_SIZE - 1] = '\0';
                    g_state.has_pending_output = 1;
                }

                if (cJSON_IsString(status) &&
                    strcmp(status->valuestring, "stopped") == 0) {
                    g_state.session_stopped = 1;
                }

                LeaveCriticalSection(&g_state.output_lock);
                cJSON_Delete(json);
            }
        }

        Sleep(POLL_SLEEP_MS);
    }

    return 0;
}

static void start_poll_thread(void)
{
    unsigned thread_id;
    HANDLE h;

    if (g_state.poll_thread != NULL) {
        return;
    }

    InitializeCriticalSection(&g_state.output_lock);

    h = (HANDLE)_beginthreadex(NULL, 0, poll_thread_func, NULL, 0, &thread_id);

    if (h == NULL) {
        DeleteCriticalSection(&g_state.output_lock);
        printf("[Note: Using synchronous polling mode]\n");
    } else {
        SetThreadPriority(h, THREAD_PRIORITY_BELOW_NORMAL);
        g_state.poll_thread = h;
    }
}

static void stop_poll_thread(void)
{
    if (g_state.poll_thread != NULL) {
        WaitForSingleObject(g_state.poll_thread, INFINITE);
        CloseHandle(g_state.poll_thread);
        g_state.poll_thread = NULL;

        DeleteCriticalSection(&g_state.output_lock);
    }
}

static int poll_sync(void)
{
    static char response[BUFFER_SIZE];
    char path[256];
    cJSON *json;
    int had_output = 0;

    if (!g_state.session_id[0]) {
        return 0;
    }

    while (handle_fileop()) {
    }

    while (handle_command()) {
    }

    handle_approval();

    snprintf(path, sizeof(path), "/output?session_id=%s", g_state.session_id);

    if (http_request("GET", path, NULL, response, sizeof(response)) ==
        HTTP_OK) {
        json = cJSON_Parse(response);
        if (json) {
            const cJSON *output = cJSON_GetObjectItem(json, "output");
            if (cJSON_IsString(output) && output->valuestring[0]) {
                printf("\r                              \r");
                print_output(output->valuestring);
                had_output = 1;
            }
            cJSON_Delete(json);
        }
    }

    return had_output;
}

static int check_pending_output(void)
{
    int had_output = 0;
    int session_ended = 0;

    if (g_state.poll_thread == NULL) {
        return poll_sync();
    }

    EnterCriticalSection(&g_state.output_lock);
    if (g_state.has_pending_output) {
        printf("\r                              \r");
        print_output(g_state.pending_output);
        g_state.has_pending_output = 0;
        had_output = 1;
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
    }

    return had_output || session_ended;
}

static void read_input_line(char *input, size_t input_size)
{
    int pos = 0;
    int prompted = 0;
    int poll_counter = 0;
    int ch;

    while (g_state.running) {
        if (!prompted) {
            printf("> ");
            fflush(stdout);
            prompted = 1;
        }

        if (g_state.poll_thread != NULL) {
            if (process_approval()) {
                prompted = 0;
            }
            if (check_pending_output()) {
                prompted = 0;
            }
        } else {
            if (++poll_counter >= POLL_INTERVAL_CYCLES) {
                poll_counter = 0;
                if (check_pending_output()) {
                    prompted = 0;
                }
            }
        }

        if (kbhit()) {
            ch = getch();

            if (ch == '\r' || ch == '\n') {
                printf("\n");
                input[pos] = '\0';
                return;
            } else if (ch == '\b' || ch == 127) {
                if (pos > 0) {
                    pos--;
                    printf("\b \b");
                    fflush(stdout);
                }
            } else if (ch == 3) {
                printf("\n[Use /quit to exit]\n");
                pos = 0;
                prompted = 0;
            } else if (ch >= 32 && ch < 127 && (size_t)pos < input_size - 1) {
                input[pos++] = (char)ch;
                printf("%c", ch);
                fflush(stdout);
            }
        } else {
            Sleep(INPUT_SLEEP_MS);
        }
    }

    input[0] = '\0';
}

static void print_banner(void)
{
    printf("==================================================\n");
    printf("  ClaudeWin9xNt - Claude Code CLI for Windows 9X/NT OSes\n");
    printf("  Type /help for commands\n");
    printf("==================================================\n");
    printf("\n");
    printf("Server: %s:%d\n", g_state.server_ip, g_state.server_port);
    printf("Status: Not connected. Type /connect to start.\n");
    printf("\n");
}

static void cleanup(void)
{
    g_state.running = 0;
    stop_poll_thread();

    if (g_state.logfile) {
        fprintf(g_state.logfile, "=== Session ended ===\n\n");
        fclose(g_state.logfile);
        g_state.logfile = NULL;
    }

    WSACleanup();
}

int main(int argc, char *argv[])
{
    WSADATA wsa;
    char input[MAX_INPUT];

    (void)argc;
    (void)argv;

    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0) {
        fprintf(stderr, "[Error: Failed to initialize Winsock]\n");
        return 1;
    }

    config_load("client.ini");

    print_banner();

    start_poll_thread();

    while (g_state.running) {
        read_input_line(input, sizeof(input));

        if (input[0] != '\0') {
            process_input(input);
        }
    }

    cleanup();
    return 0;
}
