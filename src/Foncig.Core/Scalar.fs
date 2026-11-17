namespace Foncig

open System
open System.Globalization

/// Deterministic, culture-free text for scalars, and strict parsers for reading it back.
/// Output is identical on .NET, Fable JS and Fable Python.
module Scalar =

    // ---- float formatting -------------------------------------------------------------------------------

    /// Digits (no leading/trailing zeros) and a decimal exponent `n` such that value = 0.DIGITS x 10^n.
    let private decompose (text: string) : (string * int) option =
        // text is what the platform printed: [-]digits[.digits][e|E[+-]digits]
        let t = if text.StartsWith "-" then text.Substring 1 else text

        let mantissa, exp10 =
            let i = t.IndexOfAny [| 'e'; 'E' |]

            if i < 0 then
                t, 0
            else
                let e = t.Substring(i + 1)
                let e = if e.StartsWith "+" then e.Substring 1 else e
                let neg = e.StartsWith "-"
                let digits = if neg then e.Substring 1 else e
                let mutable v = 0

                for c in digits do
                    v <- v * 10 + (int c - int '0')

                t.Substring(0, i), (if neg then -v else v)

        let intPart, fracPart =
            let d = mantissa.IndexOf '.'

            if d < 0 then
                mantissa, ""
            else
                mantissa.Substring(0, d), mantissa.Substring(d + 1)

        let all = intPart + fracPart
        let lead = all.Length - all.TrimStart('0').Length
        let digits = all.TrimStart('0').TrimEnd('0')

        if digits = "" then
            None
        else
            Some(digits, intPart.Length - lead + exp10)

    /// Canonical text for a float: shortest round-trip digits, plain notation for exponents in (-7, 21],
    /// otherwise `d.ddde+XX`. NaN, Infinity, -Infinity; negative zero is `-0`.
    let formatFloat (f: float) : string =
        if Double.IsNaN f then
            "NaN"
        elif Double.IsPositiveInfinity f then
            "Infinity"
        elif Double.IsNegativeInfinity f then
            "-Infinity"
        elif f = 0.0 then
            (if 1.0 / f < 0.0 then "-0" else "0")
        else
            let raw = f.ToString(CultureInfo.InvariantCulture)
            let sign = if f < 0.0 then "-" else ""

            match decompose raw with
            | None -> "0"
            | Some(digits, n) ->
                let k = digits.Length

                let body =
                    if k <= n && n <= 21 then
                        digits + String('0', n - k)
                    elif 0 < n && n <= 21 then
                        digits.Substring(0, n) + "." + digits.Substring n
                    elif -6 < n && n <= 0 then
                        "0." + String('0', -n) + digits
                    else
                        let e = n - 1

                        let m =
                            if k = 1 then
                                digits
                            else
                                digits.Substring(0, 1) + "." + digits.Substring 1

                        m + "e" + (if e < 0 then "-" else "+") + string (abs e)

                sign + body

    // ---- strict parsers ---------------------------------------------------------------------------------

    let private isDigit (c: char) = c >= '0' && c <= '9'

    /// `[+-]digits`, no whitespace, no separators; the whole int64 range.
    let tryInt64 (text: string) : int64 option =
        if text.Length = 0 then
            None
        else
            let neg = text.[0] = '-'
            let start = if text.[0] = '-' || text.[0] = '+' then 1 else 0

            if start >= text.Length then
                None
            elif not (text.Substring start |> Seq.forall isDigit) then
                None
            else
                // accumulate negatively so Int64.MinValue fits
                let mutable acc = 0L
                let mutable ok = true
                let mutable i = start

                while ok && i < text.Length do
                    let d = int64 (int text.[i] - int '0')

                    if acc < (Int64.MinValue + d) / 10L then
                        ok <- false
                    else
                        acc <- acc * 10L - d

                    i <- i + 1

                if not ok then None
                elif neg then Some acc
                elif acc = Int64.MinValue then None
                else Some(-acc)

    let tryInt (text: string) : int option =
        match tryInt64 text with
        | Some v when v >= int64 Int32.MinValue && v <= int64 Int32.MaxValue -> Some(int v)
        | _ -> None

    /// `[+-]digits[.digits][(e|E)[+-]digits]`, or exactly `NaN`, `Infinity`, `-Infinity`.
    let isFloatText (text: string) : bool =
        match text with
        | "NaN"
        | "Infinity"
        | "-Infinity"
        | "+Infinity" -> true
        | _ ->
            let n = text.Length
            let mutable i = 0

            if i < n && (text.[i] = '-' || text.[i] = '+') then
                i <- i + 1

            let d0 = i

            while i < n && isDigit text.[i] do
                i <- i + 1

            let intDigits = i - d0
            let mutable fracDigits = 0

            if i < n && text.[i] = '.' then
                i <- i + 1
                let f0 = i

                while i < n && isDigit text.[i] do
                    i <- i + 1

                fracDigits <- i - f0

            if intDigits + fracDigits = 0 then
                false
            else
                let mutable ok = true

                if i < n && (text.[i] = 'e' || text.[i] = 'E') then
                    i <- i + 1

                    if i < n && (text.[i] = '-' || text.[i] = '+') then
                        i <- i + 1

                    let e0 = i

                    while i < n && isDigit text.[i] do
                        i <- i + 1

                    ok <- i > e0

                ok && i = n

    let tryFloat (text: string) : float option =
        match text with
        | "NaN" -> Some Double.NaN
        | "Infinity"
        | "+Infinity" -> Some Double.PositiveInfinity
        | "-Infinity" -> Some Double.NegativeInfinity
        | _ ->
            if not (isFloatText text) then
                None
            else
                // The text is already validated by isFloatText. Fable's TryParse is always invariant and rejects the
                // culture arguments, so the call differs by target.
#if FABLE_COMPILER
                match Double.TryParse text with
                | true, v -> Some v
                | _ -> None
#else
                match Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture) with
                | true, v -> Some v
                | _ -> None
#endif

    /// `true` / `false`, any case. `1`/`0`, `yes`/`no`, `on`/`off` are NOT accepted (documented, strict).
    let tryBool (text: string) : bool option =
        match text.ToLowerInvariant() with
        | "true" -> Some true
        | "false" -> Some false
        | _ -> None

    let private isHex (c: char) =
        isDigit c || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')

    /// `8-4-4-4-12` hex digits.
    let tryGuid (text: string) : Guid option =
        if
            text.Length = 36
            && text
               |> Seq.mapi (fun i c ->
                   if i = 8 || i = 13 || i = 18 || i = 23 then
                       c = '-'
                   else
                       isHex c)
               |> Seq.forall id
        then
            try
                Some(Guid.Parse text)
            with _ ->
                None
        else
            None

    let tryUri (text: string) : Uri option =
        if text = "" || text <> text.Trim() then
            None
        else
            try
                match Uri.TryCreate(text, UriKind.Absolute) with
                | true, u -> Some u
                | _ -> None
            with _ ->
                None

    /// `[-][d.]hh:mm:ss[.fffffff]` (the .NET constant format).
    let tryTimeSpan (text: string) : TimeSpan option =
        let neg = text.StartsWith "-"
        let t = if neg then text.Substring 1 else text

        let days, rest =
            let firstColon = t.IndexOf ':'
            let dot = t.IndexOf '.'

            if dot >= 0 && firstColon > dot then
                (match tryInt (t.Substring(0, dot)) with
                 | Some d when isDigit t.[0] -> Some d
                 | _ -> None),
                t.Substring(dot + 1)
            else
                Some 0, t

        match days with
        | None -> None
        | Some d ->
            let parts = rest.Split(':')

            if parts.Length <> 3 then
                None
            else
                let secText, fracText =
                    let p = parts.[2].IndexOf '.'

                    if p < 0 then
                        parts.[2], ""
                    else
                        parts.[2].Substring(0, p), parts.[2].Substring(p + 1)

                let allDigits (s: string) = s.Length > 0 && s |> Seq.forall isDigit

                if
                    not (allDigits parts.[0] && allDigits parts.[1] && allDigits secText)
                    || not (fracText = "" || (allDigits fracText && fracText.Length <= 7))
                then
                    None
                else
                    match tryInt parts.[0], tryInt parts.[1], tryInt secText with
                    | Some h, Some m, Some s when h < 24 && m < 60 && s < 60 ->
                        let ticks =
                            if fracText = "" then
                                0L
                            else
                                (tryInt64 (fracText + String('0', 7 - fracText.Length))).Value

                        let total =
                            ((int64 d * 24L + int64 h) * 60L + int64 m) * 60L * 10000000L
                            + int64 s * 10000000L
                            + ticks

                        Some(TimeSpan(if neg then -total else total))
                    | _ -> None

    let private pad (width: int) (n: int) = (string n).PadLeft(width, '0')

    let formatTimeSpan (t: TimeSpan) : string =
        let ticks = t.Ticks
        let neg = ticks < 0L
        let a = if neg then -ticks else ticks
        let frac = int (a % 10000000L)
        let totalSeconds = a / 10000000L
        let s = int (totalSeconds % 60L)
        let totalMinutes = totalSeconds / 60L
        let m = int (totalMinutes % 60L)
        let totalHours = totalMinutes / 60L
        let h = int (totalHours % 24L)
        let d = int (totalHours / 24L)

        (if neg then "-" else "")
        + (if d > 0 then string d + "." else "")
        + pad 2 h
        + ":"
        + pad 2 m
        + ":"
        + pad 2 s
        + (if frac > 0 then "." + (pad 7 frac).TrimEnd('0') else "")

    /// ISO 8601: `yyyy-MM-dd`, optional `[T ]HH:mm[:ss[.f+]]`, optional `Z` or `+hh:mm`. No zone means UTC.
    let tryDateTimeOffset (text: string) : DateTimeOffset option =
        let n = text.Length

        let num (start: int) (len: int) =
            if start + len <= n && text.Substring(start, len) |> Seq.forall isDigit then
                tryInt (text.Substring(start, len))
            else
                None

        match num 0 4, num 5 2, num 8 2 with
        | Some y, Some mo, Some d when n >= 10 && text.[4] = '-' && text.[7] = '-' ->
            let mutable pos = 10
            let mutable h = 0
            let mutable mi = 0
            let mutable s = 0
            let mutable ticks = 0L
            let mutable ok = true

            if pos < n && (text.[pos] = 'T' || text.[pos] = ' ') then
                match num (pos + 1) 2, num (pos + 4) 2 with
                | Some hh, Some mm when pos + 5 < n && text.[pos + 3] = ':' ->
                    h <- hh
                    mi <- mm
                    pos <- pos + 6

                    if pos < n && text.[pos] = ':' then
                        match num (pos + 1) 2 with
                        | Some ss ->
                            s <- ss
                            pos <- pos + 3

                            if pos < n && text.[pos] = '.' then
                                let f0 = pos + 1
                                let mutable e = f0

                                while e < n && isDigit text.[e] do
                                    e <- e + 1

                                let frac = text.Substring(f0, e - f0)

                                if frac = "" || frac.Length > 7 then
                                    ok <- false
                                else
                                    ticks <- (tryInt64 (frac + String('0', 7 - frac.Length))).Value

                                pos <- e
                        | None -> ok <- false
                | _ -> ok <- false

            let offset =
                if not ok then
                    None
                elif pos = n then
                    Some TimeSpan.Zero
                elif text.[pos] = 'Z' && pos + 1 = n then
                    Some TimeSpan.Zero
                elif (text.[pos] = '+' || text.[pos] = '-') && pos + 6 = n && text.[pos + 3] = ':' then
                    match num (pos + 1) 2, num (pos + 4) 2 with
                    | Some oh, Some om when oh < 24 && om < 60 ->
                        let span = TimeSpan(oh, om, 0)
                        Some(if text.[pos] = '-' then span.Negate() else span)
                    | _ -> None
                else
                    None

            match offset with
            | Some off when
                mo >= 1
                && mo <= 12
                && d >= 1
                && d <= DateTime.DaysInMonth(y, mo)
                && h < 24
                && mi < 60
                && s < 60
                ->
                try
                    Some(DateTimeOffset(y, mo, d, h, mi, s, off).AddTicks ticks)
                with _ ->
                    None
            | _ -> None
        | _ -> None

    let formatDateTimeOffset (t: DateTimeOffset) : string =
        let o = t.Offset
        let totalMin = int (o.Ticks / 600000000L)
        let sign = if totalMin < 0 then "-" else "+"
        let a = abs totalMin
        let frac = int (t.Ticks % 10000000L)

        pad 4 t.Year
        + "-"
        + pad 2 t.Month
        + "-"
        + pad 2 t.Day
        + "T"
        + pad 2 t.Hour
        + ":"
        + pad 2 t.Minute
        + ":"
        + pad 2 t.Second
        + (if frac > 0 then "." + (pad 7 frac).TrimEnd('0') else "")
        + sign
        + pad 2 (a / 60)
        + ":"
        + pad 2 (a % 60)
