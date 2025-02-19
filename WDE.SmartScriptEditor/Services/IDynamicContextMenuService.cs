using System;
using System.Collections.Generic;
using System.Windows.Input;
using Prism.Commands;
using Prism.Events;
using WDE.Common.Events;
using WDE.Common.Services;
using WDE.Common.Types;
using WDE.Common.Utils;
using WDE.Module.Attributes;
using WDE.SmartScriptEditor.Data;
using WDE.SmartScriptEditor.Editor;
using WDE.SmartScriptEditor.Editor.ViewModels;
using WDE.SmartScriptEditor.Models;
using WDE.SmartScriptEditor.Settings;

namespace WDE.SmartScriptEditor.Services;

[UniqueProvider]
public interface IDynamicContextMenuService
{
    IEnumerable<INamedCommand<SmartScriptEditorViewModel>>? GenerateContextMenu(SmartScriptEditorViewModel vm, SmartAction action);
}

[AutoRegister]
[SingleInstance]
public class DynamicContextMenuService : IDynamicContextMenuService
{
    private readonly ISmartDataManager dataManager;
    private readonly ISmartFactory smartFactory;
    private readonly IEventAggregator eventAggregator;
    private readonly ISmartScriptFactory scriptFactory;
    private readonly IDbcSpellService spellService;
    private readonly IEditorFeatures editorFeatures;
    private readonly IGeneralSmartScriptSettingsProvider preferences;
    private Dictionary<int, List<Entry>> perActionMenus = new();

    private class Entry
    {
        public Entry(INamedCommand<SmartScriptEditorViewModel> command, Func<SmartAction, bool>? shouldShow)
        {
            ShouldShow = shouldShow;
            Command = command;
        }

        public Func<SmartAction, bool>? ShouldShow { get; }
        public INamedCommand<SmartScriptEditorViewModel> Command { get; }
    }
    
    public DynamicContextMenuService(ISmartDataManager dataManager,
        ISmartFactory smartFactory,
        IEventAggregator eventAggregator,
        ISmartScriptFactory scriptFactory,
        IDbcSpellService spellService,
        IEditorFeatures editorFeatures,
        IGeneralSmartScriptSettingsProvider preferences)
    {
        this.dataManager = dataManager;
        this.smartFactory = smartFactory;
        this.eventAggregator = eventAggregator;
        this.scriptFactory = scriptFactory;
        this.spellService = spellService;
        this.editorFeatures = editorFeatures;
        this.preferences = preferences;
        foreach (var actionData in dataManager.GetAllData(SmartType.SmartAction))
        {
            if (actionData.ContextMenu == null)
                continue;

            List<Entry> menus = new();
            perActionMenus[actionData.Id] = menus;
            
            foreach (var menuItem in actionData.ContextMenu)
            {
                Func<SmartAction, bool>? shouldShow = null;
                INamedCommand<SmartScriptEditorViewModel> command;
                if (menuItem.Command is SmartContextMenuCommand.AddEvent or SmartContextMenuCommand.AddEventIfAura)
                {
                    shouldShow = action => action.Source.Id <= SmartConstants.SourceSelf;

                    if (menuItem.Command == SmartContextMenuCommand.AddEventIfAura)
                    {
                        var oldShouldShow = shouldShow;
                        shouldShow = action =>
                        {
                            if (!oldShouldShow(action))
                                return false;

                            var spellId = (uint)action.GetParameter(0).Value;
                            for (int i = 0, count = spellService.GetSpellEffectsCount(spellId); i < count; ++i)
                            {
                                if (spellService.GetSpellEffectType(spellId, i) == SpellEffectType.ApplyAura)
                                    return true;
                            }

                            return false;
                        };
                    }
                    
                    if (menuItem.EventId == null)
                        command = new NamedDelegateCommand<SmartScriptEditorViewModel>(menuItem.Header, _ => {}, _ => false);
                    else
                        command = GenerateAddEventCommand(menuItem);
                }
                else if (menuItem.Command == SmartContextMenuCommand.OpenScript)
                {
                    command = GenerateOpenScriptCommand(menuItem);
                }
                else
                    command = new NamedDelegateCommand<SmartScriptEditorViewModel>(menuItem.Header, _ => {}, _ => false);
                
                menus.Add(new Entry(command, shouldShow));
            }
        }
    }

    private NamedDelegateCommand<SmartScriptEditorViewModel> GenerateOpenScriptCommand(SmartContextMenuData menuItem)
    {
        return new NamedDelegateCommand<SmartScriptEditorViewModel>(menuItem.Header, vm =>
        {
            var selectedActionIndex = vm.FirstSelectedActionIndex;
            if (selectedActionIndex.eventIndex == -1 || selectedActionIndex.actionIndex == -1)
                return;
            
            var selectedAction = vm.Events[selectedActionIndex.eventIndex].Actions[selectedActionIndex.actionIndex];
            var value = selectedAction.GetParameter(menuItem.EntryFromParameter).Value;
            
            var script = editorFeatures.HasCreatureEntry ? scriptFactory.Factory((uint)value, 0, menuItem.ScriptType) : scriptFactory.Factory(null, (int)value, menuItem.ScriptType);
            eventAggregator.GetEvent<EventRequestOpenItem>().Publish(script);
        });
    }

    private NamedDelegateCommand<SmartScriptEditorViewModel> GenerateAddEventCommand(SmartContextMenuData menuItem)
    {
        var eventData = dataManager.GetDataByName(SmartType.SmartEvent, menuItem.EventId!);
        return new NamedDelegateCommand<SmartScriptEditorViewModel>(menuItem.Header, vm =>
        {
            var selectedActionIndex = vm.FirstSelectedActionIndex;
            if (selectedActionIndex.eventIndex == -1 || selectedActionIndex.actionIndex == -1)
                return;
            
            var selectedAction = vm.Events[selectedActionIndex.eventIndex].Actions[selectedActionIndex.actionIndex];
                            
            using var _ = vm.Script.BulkEdit(menuItem.Header);
            var @event = smartFactory.EventFactory(eventData.Id);
            if (menuItem.FillParameters != null)
            {
                foreach (var param in menuItem.FillParameters)
                {
                    var destParam = @event.GetParameter(param.To);
                    destParam.Value = param.Const ?? selectedAction.GetParameter(param.From).Value;
                }
            }
            vm.Events.Insert(selectedActionIndex.eventIndex + 1, @event); // +1 to add below
            vm.DeselectAll.Execute();
            @event.IsSelected = true;
            if (preferences.InsertActionOnEventInsert)
                vm.AddActionCommand(new NewActionViewModel() { Event = @event, InsertIndex = 0 }, false).ListenErrors();
        });
    }

    public IEnumerable<INamedCommand<SmartScriptEditorViewModel>>? GenerateContextMenu(SmartScriptEditorViewModel vm, SmartAction action)
    {
        if (!perActionMenus.TryGetValue(action.Id, out var menus))
            yield break;

        foreach (var menu in menus)
        {
            if (!(menu.ShouldShow?.Invoke(action) ?? true))
                continue;

            yield return menu.Command;
        }
    }
}