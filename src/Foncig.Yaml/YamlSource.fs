namespace Foncig

open Fnode

/// YAML as a configuration file format. Register `YamlSource.format` with the host file sources.
module YamlSource =

    /// The YAML `Format` (extensions `.yaml`, `.yml`).
    let format: Format = Yaml.format

    /// A YAML document given as text.
    let fromText (name: string) (text: string) : ConfigSource = ConfigSource.fromText name format text
