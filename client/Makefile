
CC = wcc386
RC = wrc
LINKER = wlink

# -za99: C99 mode, -3: 386 target, -bt=nt: Win32 target, -ox: max optimization, -wx: max warnings
CFLAGS = -za99 -3 -bt=nt -ox -wx

TARGET = ClaudeWin9x-Client.exe
RESOURCE = ClaudeWin9xClient.rc
RESOURCE_RES = ClaudeWin9xClient.res

SOURCES = claude.c commands.c handlers.c http.c session.c transfer.c util.c
THIRD_PARTY = third_party/cJSON.c

OBJECTS = claude.obj commands.obj handlers.obj http.obj session.obj transfer.obj util.obj cJSON.obj

all: $(TARGET)

.c.obj:
	$(CC) $(CFLAGS) $<

cJSON.obj: third_party/cJSON.c
	$(CC) $(CFLAGS) -fo=cJSON.obj $<

$(RESOURCE_RES): $(RESOURCE)
	$(RC) -q -r -fo=$@ $<

$(TARGET): $(OBJECTS) $(RESOURCE_RES)
	$(LINKER) system nt name $@ file {$(OBJECTS)} library ws2_32
	$(RC) -q $(RESOURCE_RES) $@

clean: .SYMBOLIC
	-del *.obj
	-del $(RESOURCE_RES)
	-del $(TARGET)
