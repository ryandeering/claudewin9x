/*
 * util.c - Utility functions
 */

#ifndef UTIL_H
#define UTIL_H

#include "claude.h"

void path_to_backslashes(char *path);

int build_full_path(const char *relative, char *out, size_t out_size);

void get_windows_version(char *buf, size_t bufsize);

void print_output(const char *text);

void log_user_input(const char *text);

void log_error(const char *context, const char *message);

void config_load(const char *filename);

#endif /* UTIL_H */
