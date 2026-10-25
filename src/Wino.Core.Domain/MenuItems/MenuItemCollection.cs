using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Wino.Core.Domain.Collections;
using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;

namespace Wino.Core.Domain.MenuItems;

public class MenuItemCollection : ObservableRangeCollection<IMenuItem>
{
    // Which types to remove from the list when folders are changing due to selection of new account.
    // We don't clear the whole list since we want to keep the New Mail button and account menu items.
    private readonly Type[] _preservingTypesForFolderArea = [typeof(AccountMenuItem), typeof(NewMailMenuItem), typeof(MergedAccountMenuItem)];
    private readonly IDispatcher _dispatcher;

    public MenuItemCollection(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public IEnumerable<IAccountMenuItem> GetAllAccountMenuItems()
    {
        var rootItems = this.ToList();

        foreach (var item in rootItems)
        {
            if (item is MergedAccountMenuItem mergedAccountMenuItem)
            {
                foreach (var singleItem in mergedAccountMenuItem.SubMenuItems.OfType<IAccountMenuItem>().ToList())
                {
                    yield return singleItem;
                }

                yield return mergedAccountMenuItem;
            }
            else if (item is IAccountMenuItem accountMenuItem)
                yield return accountMenuItem;
        }
    }

    public IEnumerable<IBaseFolderMenuItem> GetAllFolderMenuItems(Guid folderId)
        => EnumerateAllFolderMenuItems()
            .Where(a => a is not IMailCategoryMenuItem && a is not IMergedMailCategoryMenuItem)
            .Where(a => a.HandlingFolders.Any(b => b.Id == folderId));

    public bool TryGetAccountMenuItem(Guid accountId, out IAccountMenuItem value)
    {
        var rootItems = this.ToList();

        value = rootItems.OfType<AccountMenuItem>().FirstOrDefault(a => a.AccountId == accountId);
        value ??= rootItems.OfType<MergedAccountMenuItem>().FirstOrDefault(a => a.SubMenuItems.OfType<AccountMenuItem>().Any(b => b.AccountId == accountId));

        return value != null;
    }

    // Pattern: Look for special folder menu item inside the loaded folders for Windows Mail style menu items.
    public bool TryGetWindowsStyleRootSpecialFolderMenuItem(Guid accountId, SpecialFolderType specialFolderType, out FolderMenuItem value)
    {
        value = EnumerateAllFolderMenuItems()
                .OfType<FolderMenuItem>()
                .FirstOrDefault(a => a.HandlingFolders.Any(b => b.MailAccountId == accountId && b.SpecialFolderType == specialFolderType));

        return value != null;
    }

    // Pattern: Find the merged account menu item and return the special folder menu item that belongs to the merged account menu item.
    // This will not look for the folders inside individual account menu items inside merged account menu item.
    public bool TryGetMergedAccountSpecialFolderMenuItem(Guid mergedInboxId, SpecialFolderType specialFolderType, out IBaseFolderMenuItem value)
    {
        var rootItems = this.ToList();

        value = rootItems.OfType<MergedAccountFolderMenuItem>()
                .Where(a => a.MergedInbox.Id == mergedInboxId)
                .FirstOrDefault(a => a.SpecialFolderType == specialFolderType);

        return value != null;
    }

    public bool TryGetFolderMenuItem(Guid folderId, out IBaseFolderMenuItem value)
    {
        value = EnumerateAllFolderMenuItems()
            .Where(a => a is not IMailCategoryMenuItem && a is not IMergedMailCategoryMenuItem)
            .FirstOrDefault(a => a.HandlingFolders.Any(b => b.Id == folderId));

        return value != null;
    }

    public bool TryGetCategoryMenuItem(Guid categoryId, out IBaseFolderMenuItem value)
    {
        value = EnumerateAllFolderMenuItems()
            .OfType<IMailCategoryMenuItem>()
            .FirstOrDefault(a => a.MailCategory.Id == categoryId) as IBaseFolderMenuItem;

        value ??= this.OfType<IMergedMailCategoryMenuItem>()
            .FirstOrDefault(a => a.Categories.Any(b => b.Id == categoryId)) as IBaseFolderMenuItem;

        return value != null;
    }

    public void UpdateUnreadItemCountsToZero()
    {
        foreach (var item in EnumerateAllFolderMenuItems().ToList())
        {
            item.UnreadItemCount = 0;
        }
    }

    public bool TryGetSpecialFolderMenuItem(Guid accountId, SpecialFolderType specialFolderType, out FolderMenuItem value)
    {
        value = EnumerateAllFolderMenuItems()
                .OfType<FolderMenuItem>()
                .FirstOrDefault(a => a.HandlingFolders.Any(b => b.MailAccountId == accountId && b.SpecialFolderType == specialFolderType));

        return value != null;
    }

    /// <summary>
    /// Skips the merged account menu item, but directly returns the Account menu item inside the merged account menu item.
    /// </summary>
    /// <param name="accountId">Account id to look for.</param>
    /// <returns>Direct AccountMenuItem inside the Merged Account menu item if exists.</returns>
    public AccountMenuItem GetSpecificAccountMenuItem(Guid accountId)
    {
        AccountMenuItem accountMenuItem = null;
        var rootItems = this.ToList();

        accountMenuItem = rootItems.OfType<AccountMenuItem>().FirstOrDefault(a => a.HoldingAccounts.Any(b => b.Id == accountId));

        // Look for the items inside the merged accounts if regular menu item is not found.
        accountMenuItem ??= rootItems.OfType<MergedAccountMenuItem>()
            .FirstOrDefault(a => a.HoldingAccounts.Any(b => b.Id == accountId))?.SubMenuItems
            .OfType<AccountMenuItem>()
            .FirstOrDefault(a => a.AccountId == accountId);

        return accountMenuItem;
    }

    public async Task ReplaceFoldersAsync(IEnumerable<IMenuItem> folders, IAccountMenuItem anchorAccountMenuItem = null)
    {
        await _dispatcher.ExecuteOnUIThread(() =>
        {
            ClearFolderAreaMenuItems();

            if (anchorAccountMenuItem is AccountMenuItem accountMenuItem)
            {
                foreach (var folder in folders ?? [])
                {
                    accountMenuItem.FolderItems.Add(folder);
                }

                accountMenuItem.IsExpanded = accountMenuItem.FolderItems.Count > 0;
                return;
            }

            Add(new SeperatorItem());
            AddRange(folders);
        });
    }

    /// <summary>
    /// Enables/disables account menu items in the list.
    /// </summary>
    /// <param name="isEnabled">Whether menu items should be enabled or disabled.</param>
    public async Task SetAccountMenuItemEnabledStatusAsync(bool isEnabled)
    {
        var accountItems = this.Where(a => a is IAccountMenuItem).Cast<IAccountMenuItem>().ToList();

        await _dispatcher.ExecuteOnUIThread(() =>
        {
            foreach (var item in accountItems)
            {
                item.IsEnabled = isEnabled;
            }
        });
    }

    public void AddAccountMenuItem(IAccountMenuItem accountMenuItem)
    {
        var lastAccount = Items.OfType<IAccountMenuItem>().LastOrDefault();

        // Indexes 0 and 1 are reserved for New Mail and Daily Briefing.
        var insertIndex = lastAccount == null ? 2 : Items.IndexOf(lastAccount) + 1;

        Insert(insertIndex, accountMenuItem);
    }

    public bool RemoveFolderMenuItem(Guid folderId)
    {
        foreach (var accountMenuItem in GetAllAccountMenuItems().OfType<AccountMenuItem>())
        {
            var accountFolder = accountMenuItem.FolderItems
                .OfType<IBaseFolderMenuItem>()
                .Where(a => a is not IMailCategoryMenuItem && a is not IMergedMailCategoryMenuItem)
                .FirstOrDefault(a => a.HandlingFolders.Any(b => b.Id == folderId));

            if (accountFolder != null)
            {
                accountMenuItem.FolderItems.Remove(accountFolder);
                return true;
            }

            foreach (var rootFolder in accountMenuItem.FolderItems.OfType<IBaseFolderMenuItem>().ToList())
            {
                var subItem = rootFolder.SubMenuItems
                    .OfType<IBaseFolderMenuItem>()
                    .FirstOrDefault(a => a.HandlingFolders.Any(b => b.Id == folderId));

                if (subItem != null)
                {
                    rootFolder.SubMenuItems.Remove(subItem);
                    return true;
                }
            }
        }

        // Check root-level items.
        var rootItem = this.OfType<IBaseFolderMenuItem>()
            .Where(a => a is not IMailCategoryMenuItem && a is not IMergedMailCategoryMenuItem)
            .FirstOrDefault(a => a.HandlingFolders.Any(b => b.Id == folderId));

        if (rootItem != null)
        {
            Remove(rootItem);
            return true;
        }

        // Check sub-items of root folders.
        foreach (var rootFolder in this.OfType<IBaseFolderMenuItem>().ToList())
        {
            var subItem = rootFolder.SubMenuItems
                .OfType<IBaseFolderMenuItem>()
                .FirstOrDefault(a => a.HandlingFolders.Any(b => b.Id == folderId));

            if (subItem != null)
            {
                rootFolder.SubMenuItems.Remove(subItem);
                return true;
            }
        }

        return false;
    }

    private void ClearFolderAreaMenuItems()
    {
        foreach (var accountMenuItem in GetAllAccountMenuItems().OfType<AccountMenuItem>())
        {
            accountMenuItem.FolderItems.Clear();
            accountMenuItem.IsExpanded = false;
        }

        var itemsToRemove = this.Where(a => !_preservingTypesForFolderArea.Contains(a.GetType())).ToList();

        itemsToRemove.ForEach(item =>
        {
            item.IsExpanded = false;
            item.IsSelected = false;

            try
            {
                Remove(item);
            }
            catch (Exception) { }
        });
    }

    private IEnumerable<IBaseFolderMenuItem> EnumerateAllFolderMenuItems()
    {
        foreach (var rootItem in this.OfType<IBaseFolderMenuItem>().ToList())
        {
            foreach (var item in EnumerateFolderTree(rootItem))
                yield return item;
        }

        foreach (var accountMenuItem in GetAllAccountMenuItems().OfType<AccountMenuItem>())
        {
            foreach (var folderItem in accountMenuItem.FolderItems.OfType<IBaseFolderMenuItem>().ToList())
            {
                foreach (var item in EnumerateFolderTree(folderItem))
                    yield return item;
            }
        }
    }

    private static IEnumerable<IBaseFolderMenuItem> EnumerateFolderTree(IBaseFolderMenuItem folderMenuItem)
    {
        yield return folderMenuItem;

        foreach (var child in folderMenuItem.SubMenuItems?.OfType<IBaseFolderMenuItem>().ToList() ?? [])
        {
            foreach (var item in EnumerateFolderTree(child))
                yield return item;
        }
    }
}
