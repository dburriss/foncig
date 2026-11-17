namespace Foncig

open Fnode

/// JSON as a configuration file format. Register `JsonSource.format` with the host file sources.
module JsonSource =

    /// The JSON `Format` (extension `.json`).
    let format: Format = Json.format

    /// A JSON document given as text.
    let fromText (name: string) (text: string) : ConfigSource = ConfigSource.fromText name format text
