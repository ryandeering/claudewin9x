/*
 * util.c - Utility functions
 */

#include "util.h"

static void log_output(const char *text);

static int is_switch_char(char c)
{
    return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
           (c >= '0' && c <= '9') || c == '?' || c == '-' || c == '@';
}

void path_to_backslashes(char *str)
{
    char *p = str;
    char prev = ' ';
    int in_url = 0;

    for (; *p; p++) {
        /* Detect start of URL (://) - safe because p[1]=='/' implies p[1]!='\0' */
        if (!in_url && *p == ':' && p[1] == '/' && p[2] == '/') {
            in_url = 1;
            prev = *p;
            continue;
        }

        /* Inside URL: skip until whitespace */
        if (in_url) {
            if (*p == ' ' || *p == '\t' || *p == '\n' || *p == '\r' ||
                *p == '"' || *p == '\'') {
                in_url = 0;
            }
            prev = *p;
            continue;
        }

        if (*p == '/') {
            /* Don't convert switches: space/tab followed by /switchchar */
            if ((prev == ' ' || prev == '\t') && *(p + 1) &&
                is_switch_char(*(p + 1))) {
                prev = *p;
                continue;
            }
            *p = '\\';
        }
        prev = *p;
    }
}

static int normalize_path(char *path)
{
    char *segments[128];
    int depth = 0;
    char *p;
    char *start;
    int i;

    if (path[0] && path[1] == ':') {
        start = path + 2;
        if (*start == '\\') {
            start++;
        }
    } else {
        start = path;
        if (*start == '\\') {
            start++;
        }
    }

    p = start;
    while (*p) {
        char *seg_start = p;
        char *seg_end;

        while (*p && *p != '\\') {
            p++;
        }
        seg_end = p;

        if (*p == '\\') {
            *p = '\0';
            p++;
        }

        if (seg_start == seg_end || strcmp(seg_start, ".") == 0) {
            continue;
        }

        if (strcmp(seg_start, "..") == 0) {
            if (depth == 0) {
                return -1;
            }
            depth--;
            continue;
        }

        if (depth >= 128) {
            return -1;
        }
        segments[depth++] = seg_start;
    }

    if (path[0] && path[1] == ':') {
        p = path + 2;
        *p++ = '\\';
    } else {
        p = path;
        *p++ = '\\';
    }

    for (i = 0; i < depth; i++) {
        size_t len = strlen(segments[i]);
        memmove(p, segments[i], len);
        p += len;
        if (i < depth - 1) {
            *p++ = '\\';
        }
    }
    *p = '\0';

    return 0;
}

int build_full_path(const char *relative, char *out, size_t out_size)
{
    int len;

    if (!relative || relative[0] == '\0') {
        len = snprintf(out, out_size, "C:\\");
    } else if (relative[0] == '/' || relative[0] == '\\') {
        len = snprintf(out, out_size, "C:%s", relative);
    } else {
        len = snprintf(out, out_size, "C:\\%s", relative);
    }

    if (len < 0 || (size_t)len >= out_size) {
        return -1;
    }

    path_to_backslashes(out);

    if (normalize_path(out) != 0) {
        return -1;
    }

    return 0;
}

void get_windows_version(char *buf, size_t bufsize)
{
    DWORD ver = GetVersion();
    DWORD major = (DWORD)(LOBYTE(LOWORD(ver)));
    DWORD minor = (DWORD)(HIBYTE(LOWORD(ver)));
    DWORD build = 0;

    if (ver < 0x80000000) {
        build = (DWORD)(HIWORD(ver));
    }

    if (major == 4 && minor == 0) {
        snprintf(buf, bufsize, "Windows 95");
    } else if (major == 4 && minor == 10) {
        snprintf(buf, bufsize, "Windows 98");
    } else if (major == 4 && minor == 90) {
        snprintf(buf, bufsize, "Windows ME");
    } else if (major == 5 && minor == 0) {
        snprintf(buf, bufsize, "Windows 2000 (Build %lu)", build);
    } else if (major == 5 && minor == 1) {
        snprintf(buf, bufsize, "Windows XP (Build %lu)", build);
    } else if (major == 5 && minor == 2) {
        snprintf(buf, bufsize, "Windows Server 2003 (Build %lu)", build);
    } else if (major == 6 && minor == 0) {
        snprintf(buf, bufsize, "Windows Vista (Build %lu)", build);
    } else if (major == 6 && minor == 1) {
        snprintf(buf, bufsize, "Windows 7 (Build %lu)", build);
    } else {
        snprintf(buf, bufsize, "Windows %lu.%lu (Build %lu)", major, minor,
                 build);
    }
}

void print_output(const char *text)
{
    printf("%s", text);
    fflush(stdout);
    log_output(text);
}

static void log_output(const char *text)
{
    if (g_state.logfile) {
        fprintf(g_state.logfile, "%s", text);
        fflush(g_state.logfile);
    }
}

void log_user_input(const char *text)
{
    if (g_state.logfile) {
        fprintf(g_state.logfile, "\n> %s\n", text);
        fflush(g_state.logfile);
    }
}

void log_error(const char *context, const char *message)
{
    fprintf(stderr, "[Error: %s: %s]\n", context, message);
    if (g_state.logfile) {
        fprintf(g_state.logfile, "[Error: %s: %s]\n", context, message);
        fflush(g_state.logfile);
    }
}

/*
 * Simple INI parser for config file.
 * Format:
 *   [server]
 *   ip=192.168.2.1
 *   port=5000
 */
void config_load(const char *filename)
{
    FILE *fp;
    char line[256];
    char key[64];
    char value[128];
    int in_server_section = 0;

    fp = fopen(filename, "r");
    if (!fp) {
        return;
    }

    printf("[Loading config from %s]\n", filename);

    while (fgets(line, sizeof(line), fp)) {
        char *p = line + strlen(line) - 1;
        while (p >= line && (*p == '\n' || *p == '\r' || *p == ' ')) {
            *p-- = '\0';
        }

        if (line[0] == '\0' || line[0] == ';' || line[0] == '#') {
            continue;
        }

        if (line[0] == '[') {
            in_server_section = (strncmp(line, "[server]", 8) == 0);
            continue;
        }

        if (in_server_section) {
            if (sscanf(line, "%63[^=]=%127s", key, value) == 2) {
                p = key + strlen(key) - 1;
                while (p >= key && *p == ' ') {
                    *p-- = '\0';
                }

                if (strcmp(key, "ip") == 0) {
                    strncpy(g_state.server_ip, value,
                            sizeof(g_state.server_ip) - 1);
                    g_state.server_ip[sizeof(g_state.server_ip) - 1] = '\0';
                    printf("[Config: server ip = %s]\n", g_state.server_ip);
                } else if (strcmp(key, "port") == 0) {
                    g_state.server_port = atoi(value);
                    printf("[Config: server port = %d]\n", g_state.server_port);
                } else if (strcmp(key, "skip_permissions") == 0) {
                    g_state.skip_permissions = (strcmp(value, "true") == 0 ||
                                                strcmp(value, "1") == 0);
                    printf("[Config: skip_permissions = %s]\n",
                           g_state.skip_permissions ? "true" : "false");
                }
            }
        }
    }

    fclose(fp);
}
