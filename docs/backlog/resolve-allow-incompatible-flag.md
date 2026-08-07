# Stop instructing the operator to pass a flag that does not exist

## Status

- **State:** open
- **Opened:** 2026-08-05
- **Owner:** project-owner
- **Priority:** normal
- **Story step:** *"bind a quantization to a preset"*
- **Candidate change:** `resolve-allow-incompatible-flag`

## Problem

`internal/presets/companion.go:187` refuses an incompatible companion
assignment with an error telling the operator to `pass --allow-incompatible to
assign it anyway`. No command accepts that flag. Following the instruction
produces an unrecognized option.

The origin was traced: the approved `model-preset-persistence` spec requires
only that *some* override exist, without naming it. The name was invented by an
implementation subagent inside the error string. Nothing implements it and no
spec names it.

## Deliberately narrow

An earlier version of this entry also proposed redesigning the refusal — whether
`Unknown` should refuse alongside `NotAdmissible`, and whether an override
should exist at all.

That design work is **suspended**, not answered. The `model-acquisition` use case
establishes the addressable unit as a quantization together with its companions.
If a quantization brings its companions as one unit, there is no operator-facing
act of assigning a companion, and the compatibility refusal this flag guards may
have no surface left to appear on. Settling its behaviour now would entrench a
concept the bundle work removes.

What remains is the part that is true regardless: **an error message must not
instruct the operator to use an option that no command accepts.**

## Required outcome

Either remove the suggestion from the message and refuse plainly, or implement
an override under a name the project owner chooses. Do not leave the message
pointing at an escape hatch that is not there.

## Acceptance questions

- If the message simply drops the suggestion, is the refusal still actionable —
  does it tell the operator what to do instead?
- Should this wait for the bundle work, given the surrounding concept is under
  review?

## Scope boundary

The message, and at most the flag it names. Do not redesign compatibility
evaluation; that depends on decisions the bundle work has not made yet.

## Relevant implementation areas

- `internal/presets/companion.go`
