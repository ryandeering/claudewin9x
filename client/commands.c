/*
 * commands.c - User command processing
 */

#include "commands.h"
#include "session.h"
#include "transfer.h"
#include "util.h"

static void cmd_help(void)
{
    printf("\n");
    printf("Commands:\n");
    printf("  /connect [path]   - Start Claude Code session\n");
    printf("  /disconnect       - End current session\n");
    printf("  /poll             - Manually check for output\n");
    printf("  /status           - Show connection status\n");
    printf("  /server ip:port   - Set server address\n");
    printf("  /log [on|off|view]- Logging: on/off or view log file\n");
    printf("  /download <remote> <local> - Download file from proxy\n");
    printf("  /upload <local> <remote>   - Upload file to proxy\n");
    printf("  /clear            - Clear screen\n");
    printf("  /quit             - Exit program\n");
    printf("\n");
}

static void cmd_server(const char *addr)
{
    char addr_copy[128];
    char *colon;
    char new_ip[64];
    int new_port;

    strncpy(addr_copy, addr, sizeof(addr_copy) - 1);
    addr_copy[sizeof(addr_copy) - 1] = '\0';

    colon = strchr(addr_copy, ':');
    if (colon) {
        *colon = '\0';
        snprintf(new_ip, sizeof(new_ip), "%s", addr_copy);
        new_port = atoi(colon + 1);
    } else {
        snprintf(new_ip, sizeof(new_ip), "%s", addr_copy);
        new_port = PORT_API;
    }

    if (g_state.poll_thread != NULL) {
        EnterCriticalSection(&g_state.output_lock);
    }
    strncpy(g_state.server_ip, new_ip, sizeof(g_state.server_ip) - 1);
    g_state.server_ip[sizeof(g_state.server_ip) - 1] = '\0';
    g_state.server_port = new_port;
    if (g_state.poll_thread != NULL) {
        LeaveCriticalSection(&g_state.output_lock);
    }

    printf("[Server set to %s:%d]\n", g_state.server_ip, g_state.server_port);
}

static void cmd_log(const char *arg)
{
    if (!arg || !arg[0] || strcmp(arg, "on") == 0) {
        if (g_state.logfile) {
            printf("[Logging already enabled to %s]\n", g_state.logpath);
            return;
        }

        g_state.logfile = fopen(g_state.logpath, "a");
        if (g_state.logfile) {
            printf("[Logging enabled to %s]\n", g_state.logpath);
            fprintf(g_state.logfile, "\n=== Session started ===\n");
            fflush(g_state.logfile);
        } else {
            log_error("log", "Could not open log file");
        }
    } else if (strcmp(arg, "off") == 0) {
        if (g_state.logfile) {
            fprintf(g_state.logfile, "=== Session ended ===\n\n");
            fclose(g_state.logfile);
            g_state.logfile = NULL;
            printf("[Logging disabled]\n");
        } else {
            printf("[Logging already disabled]\n");
        }
    } else if (strcmp(arg, "view") == 0) {
        char cmd[512];
        snprintf(cmd, sizeof(cmd), "edit %s", g_state.logpath);
        printf("[Opening %s...]\n", g_state.logpath);
        system(cmd);
    } else {
        printf("[Usage: /log [on|off|view]]\n");
    }
}

static void cmd_status(void)
{
    printf("\n");
    printf("Server: %s:%d\n", g_state.server_ip, g_state.server_port);

    if (g_state.connected) {
        printf("Status: Connected\n");
        printf("Session: %s\n", g_state.session_id);
    } else {
        printf("Status: Not connected\n");
    }

    printf("\n");
}

static void cmd_download(const char *args)
{
    char remote_path[256];
    char local_path[256];

    if (sscanf(args, "%255s %255s", remote_path, local_path) != 2) {
        printf("[Usage: /download <remote_path> <local_path>]\n");
        printf(
            "[Example: /download client/claude.exe C:\\CLAUDE\\CLAUDE.EXE]\n");
        return;
    }

    transfer_download(remote_path, local_path);
}

static void cmd_upload(const char *args)
{
    char local_path[256];
    char remote_path[256];

    if (sscanf(args, "%255s %255s", local_path, remote_path) != 2) {
        printf("[Usage: /upload <local_path> <remote_path>]\n");
        printf("[Example: /upload C:\\MYFILE.TXT myfile.txt]\n");
        return;
    }

    transfer_upload(local_path, remote_path);
}

void process_input(char *input)
{
    size_t len;

    len = strlen(input);
    while (len > 0 && (input[len - 1] == '\n' || input[len - 1] == '\r')) {
        input[--len] = '\0';
    }

    if (len == 0) {
        return;
    }

    if (input[0] == '/') {
        if (strcmp(input, "/help") == 0) {
            cmd_help();
        } else if (strcmp(input, "/connect") == 0) {
            session_connect(NULL);
        } else if (strncmp(input, "/connect ", 9) == 0) {
            session_connect(input + 9);
        } else if (strcmp(input, "/disconnect") == 0) {
            session_disconnect();
        } else if (strcmp(input, "/status") == 0) {
            cmd_status();
        } else if (strcmp(input, "/poll") == 0) {
            session_poll_once();
        } else if (strncmp(input, "/server ", 8) == 0) {
            cmd_server(input + 8);
        } else if (strcmp(input, "/log") == 0) {
            cmd_log(NULL);
        } else if (strncmp(input, "/log ", 5) == 0) {
            cmd_log(input + 5);
        } else if (strcmp(input, "/clear") == 0) {
            system("cls");
        } else if (strncmp(input, "/download ", 10) == 0) {
            cmd_download(input + 10);
        } else if (strncmp(input, "/upload ", 8) == 0) {
            cmd_upload(input + 8);
        } else if (strcmp(input, "/quit") == 0 || strcmp(input, "/exit") == 0) {
            if (g_state.connected) {
                session_disconnect();
            }
            g_state.running = 0;
        } else {
            printf("[Unknown command. Type /help for help]\n");
        }
    } else {
        session_send_input(input);
    }
}
