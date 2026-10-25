# Security policy

NexusKeypad drives real desktop applications and reads local agent sessions, so these issues matter: a way to
make a key press send a message, approve an action or submit anything (pastes and dictation must always stay unsent),
to reach the local control pipe from another user or from the network, to make the plugin connect anywhere other than
`127.0.0.1` or the page a key press asks for, or to leak session content, screen captures or phone photos outside
`~/.nexus/trinite`.

Please report such issues privately through
[GitHub private vulnerability reporting](https://github.com/pironjulien/NexusKeypad/security/advisories/new),
not in a public issue. Include the key pressed, the app involved and what happened. You will get an answer within a
few days.

Only the latest version on `main` is supported.
