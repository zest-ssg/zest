// _prebuild.fsx — Zest pre-build script.
//
// Runs once per build, after _config.toml is loaded and before the site is
// built. It must sit at the project root under exactly this name; it is never
// discovered in subdirectories, never routed, and never published.
//
// Its job is to shape what gets rendered. It has no access to the build
// output; anything that runs after the site exists belongs in _finalize.fsx,
// and anything declarative belongs in _config.toml.
//
// Available helpers:
//
//   addGlobal key value            inject a value into global data
//   addGlobalFunction name value   expose a value to templates
//   addFilter name spec            register a Zestucks filter pipeline
//   loadJson path                  parse a JSON file
//   loadToml path                  parse a TOML file
//   loadEnv key                    read an environment variable
//   console_log message            print to stderr
//   exec command args              run a shell command, return its result
//
// Examples — uncomment what you need.
//
// Expose the build year to templates as {{ site.buildYear }}:
//
//   addGlobal "buildYear" (System.DateTime.UtcNow.Year)
//
// Load a JSON file into global data:
//
//   addGlobal "authors" (loadJson "_data/authors.json")
//
// Register a custom filter usable as {{ title | shout }}:
//
//   addFilter "shout" "upper"
//
// Inject the current commit as {{ site.commit }}:
//
//   match (exec "git" [ "rev-parse"; "--short"; "HEAD" ]).code with
//   | 0 -> addGlobal "commit" ((exec "git" [ "rev-parse"; "--short"; "HEAD" ]).stdout.Trim())
//   | _ -> console_log "not a git checkout"
