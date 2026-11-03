# Unified memo basket

Approved scope: one rope basket for image, text and combined memos; no envelope or duplicate tray entries. Keep existing data, colors, order and per-item archive settings. Image/combined auto-hang and text auto-hang stay independent, stacked in basket header.

1. Extend BasketWindow with shared TextCollection, mixed list, type/search/pending filters, current memo details and debounced transactional autosave. Preserve lazy image loading and anchored popup lifecycle. New blank draft creates a text record only on nonblank input; failures preserve drafts and block switching/closing.
2. Make row summaries show thumbnail/type/text, right pane show image and editable full text, image double-click open full original, existing per-item archive switch target current key. Keep bulk hang/delete/select-all for all types.
3. Remove envelope and duplicate tray controls from visible UI; route legacy text-entry commands to the unified basket. Basket count includes every memo. Do not change clipboard switches or saved contents.
4. Add mixed-memo integration tests: all/type/search/pending filters, image/text preview, direct new text, autosave/failure recovery, independent auto-hang controls, single-item archive key, mixed bulk operations, and removed visible entries. Update old integration expectations.
5. Run relevant suites, review source and rendered UI, build package and back up/update local application. No new GitHub release requested for this change.

Confirmed follow-up: single toolbar row; cancellable image/text/combined filter buttons; compact three-column thumbnail grid with whole-card multiselect; horizontal independent auto-hang switches. UnifiedBasketTests31, card integration25 and installer checks validated. User subsequently requested publishing all current changes as 1.3.0.
