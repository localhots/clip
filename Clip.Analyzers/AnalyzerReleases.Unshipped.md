### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CLIP001 | Usage | Error | Invalid fields argument — primitives, strings, and arrays throw at runtime
CLIP002 | Usage | Warning | Message contains template syntax — Clip uses plain messages, not templates
CLIP003 | Usage | Warning | AddContext return value discarded — context scope will leak
CLIP004 | Usage | Info | Exception not passed to Error in catch block
CLIP005 | Usage | Warning | Unreachable code after Fatal — process terminates
CLIP006 | Usage | Warning | Interpolated string in log message — defeats structured logging
CLIP007 | Usage | Info | Exception passed as fields — use Error(message, exception) overload
CLIP008 | Usage | Info | Empty or whitespace log message
CLIP009 | Usage | Info | Log message starts with lowercase letter
