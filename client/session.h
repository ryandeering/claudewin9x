/*
 * session.c - Session management
 */

#ifndef SESSION_H
#define SESSION_H

#include "claude.h"

void session_connect(const char *working_dir);

void session_disconnect(void);

void session_send_input(const char *text);

void session_heartbeat(void);

void session_poll_once(void);

#endif /* SESSION_H */
