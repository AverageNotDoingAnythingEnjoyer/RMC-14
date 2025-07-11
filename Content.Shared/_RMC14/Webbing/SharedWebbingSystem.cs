﻿using System.Linq;
using Content.Shared._RMC14.Xenonids;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using static Content.Shared._RMC14.Webbing.WebbingTransferComponent;

namespace Content.Shared._RMC14.Webbing;

public abstract class SharedWebbingSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ClothingBlockWebbingComponent, BeingEquippedAttemptEvent>(OnBlockWebbingBeingEquippedAttempt);

        SubscribeLocalEvent<WebbingClothingComponent, MapInitEvent>(OnWebbingClothingMapInit);
        SubscribeLocalEvent<WebbingClothingComponent, InteractUsingEvent>(OnWebbingClothingInteractUsing);
        SubscribeLocalEvent<WebbingClothingComponent, InventoryRelayedEvent<GetVerbsEvent<EquipmentVerb>>>(GetRelayedVerbs);
        SubscribeLocalEvent<WebbingClothingComponent, GetVerbsEvent<EquipmentVerb>>(OnWebbingClothingGetEquipmentVerbs);
        SubscribeLocalEvent<WebbingClothingComponent, GetVerbsEvent<InteractionVerb>>(OnWebbingClothingGetInteractionVerbs);
        SubscribeLocalEvent<WebbingClothingComponent, EntInsertedIntoContainerMessage>(OnClothingInserted);
        SubscribeLocalEvent<WebbingClothingComponent, EntRemovedFromContainerMessage>(OnClothingRemoved);
        SubscribeLocalEvent<WebbingClothingComponent, BeingEquippedAttemptEvent>(OnClothingBeingEquippedAttempt);
    }

    private void OnBlockWebbingBeingEquippedAttempt(Entity<ClothingBlockWebbingComponent> ent, ref BeingEquippedAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var slots = _inventory.GetSlotEnumerator(args.EquipTarget);
        while (slots.MoveNext(out var slot))
        {
            if (!TryComp(slot.ContainedEntity, out WebbingClothingComponent? clothing) ||
                clothing.Webbing == null)
            {
                continue;
            }

            args.Reason = "rmc-webbing-cannot-wear-with-webbing";
            args.Cancel();
        }
    }

    private void OnWebbingClothingMapInit(Entity<WebbingClothingComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.StartingWebbing is not { } starting)
            return;

        var webbing = Spawn(starting, MapCoordinates.Nullspace);
        Attach(ent, webbing, null, out _);
    }

    private void OnWebbingClothingInteractUsing(Entity<WebbingClothingComponent> clothing, ref InteractUsingEvent args)
    {
        Attach(clothing, args.Used, args.User, out var handled);
        args.Handled = handled;
    }

    private void OnWebbingClothingGetInteractionVerbs(Entity<WebbingClothingComponent> clothing, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || HasComp<XenoComponent>(args.User))
            return;

        if (!HasWebbing((clothing, clothing), out _))
            return;

        var user = args.User;
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("rmc-storage-webbing-remove-verb"),
            Act = () => Detach(clothing, user),
            IconEntity = GetNetEntity(clothing.Owner)
        });
    }

    private void GetRelayedVerbs(EntityUid uid, WebbingClothingComponent component, InventoryRelayedEvent<GetVerbsEvent<EquipmentVerb>> args)
    {
        OnWebbingClothingGetEquipmentVerbs((uid, component), ref args.Args);
    }

    private void OnWebbingClothingGetEquipmentVerbs(Entity<WebbingClothingComponent> clothing, ref GetVerbsEvent<EquipmentVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || HasComp<XenoComponent>(args.User))
            return;

        if (!HasWebbing((clothing, clothing), out _))
            return;

        var wearer = Transform(clothing).ParentUid;
        var user = args.User;

        // To avoid duplicate verbs
        if (user == wearer)
            return;

        // To prevent stripping webbing from alive players
        if (!_mob.IsDead(wearer))
            return;

        args.Verbs.Add(new EquipmentVerb
        {
            Text = Loc.GetString("rmc-storage-webbing-remove-verb"),
            Act = () => Detach(clothing, user),
            IconEntity = GetNetEntity(clothing.Owner)
        });
    }

    public bool HasWebbing(Entity<WebbingClothingComponent?> clothing, out Entity<WebbingComponent> webbing)
    {
        webbing = default;
        if (!Resolve(clothing, ref clothing.Comp, false))
            return false;

        if (!_container.TryGetContainer(clothing, clothing.Comp.Container, out var container) ||
            container.Count <= 0)
        {
            return false;
        }

        var ent = container.ContainedEntities[0];
        if (!TryComp(ent, out WebbingComponent? webbingComp))
        {
            return false;
        }

        webbing = (ent, webbingComp);
        return true;
    }

    protected virtual void OnClothingInserted(Entity<WebbingClothingComponent> clothing, ref EntInsertedIntoContainerMessage args)
    {
        if (clothing.Comp.Container != args.Container.ID)
            return;

        clothing.Comp.Webbing = args.Entity;
        Dirty(clothing);
        _item.VisualsChanged(clothing);
    }

    protected virtual void OnClothingRemoved(Entity<WebbingClothingComponent> clothing, ref EntRemovedFromContainerMessage args)
    {
        if (clothing.Comp.Container != args.Container.ID)
            return;

        clothing.Comp.Webbing = null;
        Dirty(clothing);
        _item.VisualsChanged(clothing);
    }

    private void OnClothingBeingEquippedAttempt(Entity<WebbingClothingComponent> ent, ref BeingEquippedAttemptEvent args)
    {
        if (ent.Comp.Webbing == null)
            return;

        var slots = _inventory.GetSlotEnumerator(args.EquipTarget);
        while (slots.MoveNext(out var slot))
        {
            if (!HasComp<ClothingBlockWebbingComponent>(slot.ContainedEntity))
                continue;

            args.Reason = "rmc-webbing-cannot-wear-with-webbing";
            args.Cancel();
        }
    }

    public bool Attach(Entity<WebbingClothingComponent> clothing, EntityUid webbing, EntityUid? user, out bool handled)
    {
        handled = false;
        if (!TryComp(webbing, out WebbingComponent? webbingComp) ||
            HasComp<StorageComponent>(clothing) ||
            !HasComp<StorageComponent>(webbing) ||
            !TryComp(clothing, out ItemComponent? clothingItem) ||
            !TryComp(webbing, out ItemComponent? webbingItem))
        {
            return false;
        }

        if (_container.TryGetContainingContainer((clothing, null), out var containing))
        {
            if (TryComp(containing.Owner, out StorageComponent? storage) &&
                storage.StoredItems.ContainsKey(clothing))
            {
                handled = true;

                if (user != null)
                    _popup.PopupClient(Loc.GetString("rmc-webbing-cannot-in-storage"), user, PopupType.LargeCaution);

                return false;
            }

            if (TryComp(containing.Owner, out InventoryComponent? inventory))
            {
                var slots = _inventory.GetSlotEnumerator((containing.Owner, inventory));
                while (slots.MoveNext(out var slot))
                {
                    if (HasComp<ClothingBlockWebbingComponent>(slot.ContainedEntity))
                    {
                        handled = true;

                        if (user != null)
                            _popup.PopupClient(Loc.GetString("rmc-webbing-cannot-wear-with-webbing"), webbing, user, PopupType.SmallCaution);
                        return false;
                    }
                }
            }
        }

        var container = _container.EnsureContainer<ContainerSlot>(clothing, clothing.Comp.Container);
        if (container.Count > 0 || !_container.Insert(webbing, container))
            return false;

        EntityManager.AddComponents(clothing, webbingComp.Components);

        var comp = EnsureComp<WebbingTransferComponent>(webbing);
        comp.Clothing = clothing;
        comp.Transfer = TransferType.ToClothing;
        Dirty(webbing, comp);

        clothing.Comp.UnequippedSize = clothingItem.Size;
        _item.SetSize(clothing, webbingItem.Size);

        handled = true;
        return true;
    }

    private void Detach(Entity<WebbingClothingComponent> clothing, EntityUid user)
    {
        if (TerminatingOrDeleted(clothing) || !clothing.Comp.Running)
            return;

        if (!HasWebbing((clothing, clothing), out var webbing))
            return;

        _container.TryRemoveFromContainer(webbing.Owner);
        _hands.TryPickupAnyHand(user, webbing);

        EntityManager.AddComponents(webbing, webbing.Comp.Components);

        var comp = EnsureComp<WebbingTransferComponent>(webbing);
        comp.Clothing = clothing;
        comp.Transfer = TransferType.ToWebbing;
        Dirty(webbing, comp);

        if (clothing.Comp.UnequippedSize is { } size)
        {
            clothing.Comp.UnequippedSize = null;
            _item.SetSize(clothing, size);
        }
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<WebbingTransferComponent, WebbingComponent>();
        while (query.MoveNext(out var uid, out var transfer, out var webbing))
        {
            // TODO RMC14 remove this once the bug with transferring on same tick upstream is fixed
            if (transfer.Defer)
            {
                transfer.Defer = false;
                continue;
            }

            RemCompDeferred<WebbingTransferComponent>(uid);

            switch (transfer.Transfer)
            {
                case TransferType.ToClothing:
                {
                    if (!TryComp(uid, out StorageComponent? storage) ||
                        transfer.Clothing is not { } clothing)
                    {
                        continue;
                    }

                    foreach (var stored in storage.Container.ContainedEntities.ToArray())
                    {
                        _storage.Insert(clothing, stored, out _, playSound: false);
                    }

                    break;
                }
                case TransferType.ToWebbing:
                {
                    if (transfer.Clothing is not { } clothing)
                        continue;

                    if (TryComp(clothing, out StorageComponent? storage))
                    {
                        foreach (var stored in storage.Container.ContainedEntities.ToArray())
                        {
                            _storage.Insert(uid, stored, out _, playSound: false);
                        }
                    }

                    foreach (var entry in webbing.Components.Values)
                    {
                        RemComp(clothing, entry.Component.GetType());
                    }

                    break;
                }
            }
        }
    }
}
