# Plan: Operations container details save/edit diagnosis

## Goal
Explain and fix why saving container details/number in the Operations screen is rejected, and why editing after save is blocked.

## Findings
- `OperationForm.razor` has no UI for container details/number; container details (`ContainerDetails`) belong to `BookingForm` (booking container lines).
- Operations only copy booking container details (`OperationsController` lines ~1014+).
- Edit is blocked when `_locked = _status is "Closed" or "Cancelled"`; save button gated by `CanSave = _id is null ? CanCreate : CanEdit` (permissions `OPERATION.CREATE/EDIT`).
- If user enters container number in a "container details" dialog that actually targets booking, save may be rejected by server when `ContainerNumber`/`ContainerId` both null (skipped) or duplicate container number (unique constraint).

## Proposed changes
1. Clarify in UI/help that container details are entered in Booking screen, not Operations.
2. If Operations should support container details, add `ContainerDetails` to Operation DTO + UI + controller handling (mirror Booking).
3. Unlock edit for non-terminal statuses; ensure `OPERATION.EDIT` permission granted.

## Risks
- Schema/DTO changes; migration needed.

## Validation
- Create booking with container number; create operation from booking; verify copy.
- Test edit on Pending operation with OPERATION.EDIT permission.
