(function () {
    'use strict';

    var MENU_DESKTOP_ID = 'aw-topbar-entry';
    var MENU_USERMENU_ID = 'aw-usermenu-entry';
    var MENU_MOBILEDRAWER_ID = 'aw-mobiledrawer-entry';
    var MENU_LEGACY_ID = 'aw-sidebar-entry';
    var MODAL_ID = 'aw-modal-overlay';

    function getApiClient() {
        if (typeof ApiClient !== 'undefined' && ApiClient) return ApiClient;
        if (typeof window.ApiClient !== 'undefined' && window.ApiClient) return window.ApiClient;
        return null;
    }

    function waitForApiClient(callback) {
        var api = getApiClient();
        if (api) {
            callback(api);
            return;
        }
        var attempts = 0;
        var interval = setInterval(function () {
            api = getApiClient();
            if (api || attempts > 100) {
                clearInterval(interval);
                if (api) callback(api);
            }
            attempts++;
        }, 200);
    }

    // ── 1. Target: Modern Desktop Top Navigation (UserViewNav / AppToolbar) ──────
    function injectDesktopTopBar() {
        if (document.getElementById(MENU_DESKTOP_ID)) return;

        // In Jellyfin 12 Modern Layout, libraries and links are rendered in UserViewNav.
        // /home?tab=1 (Favorites) is the standard anchor button in the top navigation stack.
        var favLink = document.querySelector('header a[href*="/home?tab=1"], .skinHeader a[href*="/home?tab=1"], header a[href*="tab=1"]');
        var container = null;
        if (favLink && favLink.parentElement) {
            container = favLink.parentElement;
        } else {
            container = document.querySelector('header [class*="MuiStack-root"], .skinHeader [class*="MuiStack-root"]');
        }

        if (!container) return;

        var btn = document.createElement('button');
        btn.id = MENU_DESKTOP_ID;
        btn.type = 'button';
        btn.className = 'MuiButtonBase-root MuiButton-root MuiButton-text MuiButton-textInherit MuiButton-sizeMedium MuiButton-textSizeMedium MuiButton-colorInherit';
        btn.style.cssText = 'min-width:auto;padding:6px 10px;text-transform:none;font-weight:500;font-size:0.875rem;line-height:1.75;letter-spacing:0.02857em;display:inline-flex;align-items:center;vertical-align:middle;color:inherit;background:none;border:none;cursor:pointer;border-radius:4px;transition:background-color 250ms cubic-bezier(0.4, 0, 0.2, 1);font-family:inherit;';
        btn.setAttribute('aria-label', 'AniWorld Downloader');
        btn.title = 'AniWorld Downloader';
        btn.innerHTML = '<span class="MuiButton-icon MuiButton-startIcon MuiButton-iconSizeMedium" style="display:inherit;margin-right:6px;margin-left:-2px;"><span class="material-icons" style="font-size:20px;">download</span></span><span>AniWorld</span>';

        btn.addEventListener('mouseenter', function () {
            this.style.backgroundColor = 'rgba(255, 255, 255, 0.08)';
        });
        btn.addEventListener('mouseleave', function () {
            this.style.backgroundColor = 'transparent';
        });
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            showModal();
        });

        container.appendChild(btn);
    }

    // ── 2. Target: Modern User Menu (#app-user-menu / Avatar Dropdown) ───────────
    function injectUserMenu() {
        if (document.getElementById(MENU_USERMENU_ID)) return;

        var userMenu = document.getElementById('app-user-menu');
        if (!userMenu) return;

        // Find the settings link (/mypreferencesmenu) or the main list
        var settingsItem = userMenu.querySelector('a[href*="mypreferencesmenu"], a[href*="preferences"]');
        var menuList = userMenu.querySelector('ul[role="menu"], ul.MuiMenu-list, ul');
        if (!settingsItem && !menuList) return;

        var item = document.createElement('li');
        item.id = MENU_USERMENU_ID;
        item.className = 'MuiButtonBase-root MuiMenuItem-root MuiMenuItem-gutters';
        item.tabIndex = -1;
        item.setAttribute('role', 'menuitem');
        item.style.cssText = 'cursor:pointer;display:flex;align-items:center;padding:8px 16px;text-decoration:none;color:inherit;transition:background-color 150ms cubic-bezier(0.4, 0, 0.2, 1);font-family:inherit;';
        item.innerHTML = '<div class="MuiListItemIcon-root" style="min-width:36px;display:inline-flex;color:inherit;"><span class="material-icons" style="font-size:20px;">download</span></div><div class="MuiListItemText-root"><span class="MuiTypography-root MuiTypography-body1 MuiListItemText-primary" style="font-size:0.95rem;">AniWorld Downloader</span></div>';

        item.addEventListener('mouseenter', function () {
            this.style.backgroundColor = 'rgba(255, 255, 255, 0.08)';
        });
        item.addEventListener('mouseleave', function () {
            this.style.backgroundColor = 'transparent';
        });
        item.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();

            // Dismiss the MUI popover
            var backdrop = userMenu.querySelector('.MuiBackdrop-root') || document.querySelector('#app-user-menu ~ .MuiBackdrop-root, .MuiPopover-root .MuiBackdrop-root');
            if (backdrop) {
                backdrop.click();
            } else {
                document.dispatchEvent(new MouseEvent('click', { bubbles: true }));
            }

            showModal();
        });

        if (settingsItem && settingsItem.parentNode) {
            settingsItem.parentNode.insertBefore(item, settingsItem.nextSibling);
        } else if (menuList) {
            menuList.appendChild(item);
        }
    }

    // ── 3. Target: Modern Mobile Drawer (AppDrawer / MainDrawerContent) ──────────
    function injectMobileDrawer() {
        if (document.getElementById(MENU_MOBILEDRAWER_ID)) return;

        var drawerPaper = document.querySelector('.MuiDrawer-paper, div[class*="MuiDrawer-paper"]');
        if (!drawerPaper) return;

        var firstList = drawerPaper.querySelector('ul.MuiList-root, ul');
        if (!firstList) return;

        var li = document.createElement('li');
        li.id = MENU_MOBILEDRAWER_ID;
        li.className = 'MuiListItem-root MuiListItem-gutters';
        li.style.cssText = 'padding:0;display:block;';

        var btn = document.createElement('div');
        btn.className = 'MuiButtonBase-root MuiListItemButton-root MuiListItemButton-gutters';
        btn.tabIndex = 0;
        btn.setAttribute('role', 'button');
        btn.style.cssText = 'cursor:pointer;width:100%;display:flex;align-items:center;padding:8px 16px;box-sizing:border-box;color:inherit;transition:background-color 150ms cubic-bezier(0.4, 0, 0.2, 1);font-family:inherit;';
        btn.innerHTML = '<div class="MuiListItemIcon-root" style="min-width:40px;display:inline-flex;color:inherit;"><span class="material-icons" style="font-size:24px;">download</span></div><div class="MuiListItemText-root"><span class="MuiTypography-root MuiTypography-body1 MuiListItemText-primary">AniWorld Downloader</span></div>';

        btn.addEventListener('mouseenter', function () {
            this.style.backgroundColor = 'rgba(255, 255, 255, 0.08)';
        });
        btn.addEventListener('mouseleave', function () {
            this.style.backgroundColor = 'transparent';
        });
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();

            // Dismiss mobile drawer
            var drawerBackdrop = document.querySelector('.MuiDrawer-root .MuiBackdrop-root, .MuiModal-root .MuiBackdrop-root');
            if (drawerBackdrop) drawerBackdrop.click();

            showModal();
        });

        li.appendChild(btn);
        firstList.appendChild(li);
    }

    // ── 4. Target: Classic / Legacy Sidebar (.mainDrawer-scrollContainer) ────────
    function injectLegacySidebar() {
        if (document.getElementById(MENU_LEGACY_ID)) return;

        var sidebar = document.querySelector('.mainDrawer-scrollContainer');
        if (!sidebar) return;

        // In Jellyfin 12 Modern layout, a dummy hidden .mainDrawer-scrollContainer is kept with display: none.
        // Ignore it if it's hidden.
        if (sidebar.offsetParent === null && window.getComputedStyle(sidebar).display === 'none') {
            return;
        }
        var parent = sidebar.parentElement;
        if (parent && window.getComputedStyle(parent).display === 'none') {
            return;
        }

        var customSection = sidebar.querySelector('.customMenuOptions');
        var adminSection = sidebar.querySelector('.adminMenuOptions');

        var entry = document.createElement('a');
        entry.id = MENU_LEGACY_ID;
        entry.className = 'navMenuOption lnkMediaFolder';
        entry.href = '#';
        entry.setAttribute('data-itemid', 'aniworld');
        entry.innerHTML = '<span class="material-icons navMenuOptionIcon download" aria-hidden="true"></span>' +
            '<span class="navMenuOptionText">AniWorld Downloader</span>';

        entry.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();

            var backdrop = document.querySelector('.mainDrawer-backdrop');
            if (backdrop) backdrop.click();

            showModal();
        });

        if (customSection) {
            customSection.appendChild(entry);
        } else if (adminSection) {
            adminSection.parentNode.insertBefore(entry, adminSection);
        } else {
            sidebar.appendChild(entry);
        }
    }

    // ── Modal UI & Lifecycle ──────────────────────────────────────────────────
    function showModal() {
        var existing = document.getElementById(MODAL_ID);
        if (existing) {
            existing.style.display = 'flex';
            return;
        }

        // Create full-screen modal with z-index above all MUI AppBars (1100), Drawers (1200), and Dialogs (1300)
        var overlay = document.createElement('div');
        overlay.id = MODAL_ID;
        overlay.style.cssText = 'position:fixed;top:0;left:0;right:0;bottom:0;z-index:10000;display:flex;flex-direction:column;background:#181818;color:#eee;font-family:inherit;-webkit-font-smoothing:antialiased;';

        // Header matching modern Jellyfin dark theme
        var header = document.createElement('div');
        header.style.cssText = 'display:flex;align-items:center;justify-content:space-between;padding:0.6em 1.2em;background:#101010;border-bottom:1px solid rgba(255,255,255,0.08);flex-shrink:0;box-shadow:0 2px 8px rgba(0,0,0,0.5);';

        var titleContainer = document.createElement('div');
        titleContainer.style.cssText = 'display:flex;align-items:center;gap:0.6em;';

        var icon = document.createElement('span');
        icon.className = 'material-icons';
        icon.textContent = 'download';
        icon.style.cssText = 'font-size:1.4em;color:#00a4dc;';

        var title = document.createElement('span');
        title.textContent = 'AniWorld Downloader';
        title.style.cssText = 'font-size:1.15em;font-weight:600;color:#fff;letter-spacing:0.02em;';

        titleContainer.appendChild(icon);
        titleContainer.appendChild(title);

        var closeBtn = document.createElement('button');
        closeBtn.innerHTML = '<span class="material-icons" style="font-size:1.4em;">close</span>';
        closeBtn.title = 'Close';
        closeBtn.setAttribute('aria-label', 'Close');
        closeBtn.style.cssText = 'background:none;border:none;color:#fff;cursor:pointer;padding:0.35em;border-radius:50%;display:flex;align-items:center;opacity:0.75;transition:opacity 150ms,background-color 150ms;';
        closeBtn.addEventListener('mouseenter', function () {
            this.style.opacity = '1';
            this.style.backgroundColor = 'rgba(255,255,255,0.1)';
        });
        closeBtn.addEventListener('mouseleave', function () {
            this.style.opacity = '0.75';
            this.style.backgroundColor = 'transparent';
        });
        closeBtn.addEventListener('click', hideModal);

        header.appendChild(titleContainer);
        header.appendChild(closeBtn);

        // Scrollable content
        var content = document.createElement('div');
        content.id = 'aw-modal-content';
        content.style.cssText = 'flex:1;overflow-y:auto;background:#181818;color:#eee;';

        // Modern loading spinner
        content.innerHTML = '<div style="display:flex;flex-direction:column;align-items:center;justify-content:center;padding:4em 2em;opacity:0.75;">' +
            '<span style="display:inline-block;width:2em;height:2em;border:3px solid rgba(255,255,255,0.15);border-top-color:#00a4dc;border-radius:50%;animation:aw-spin 0.8s linear infinite;margin-bottom:1em;"></span>' +
            '<span style="font-size:1em;letter-spacing:0.03em;">Loading AniWorld Downloader...</span>' +
            '</div><style>@keyframes aw-spin{to{transform:rotate(360deg);}}</style>';

        overlay.appendChild(header);
        overlay.appendChild(content);

        // Close on Escape key
        var escHandler = function (e) {
            if (e.key === 'Escape') {
                document.removeEventListener('keydown', escHandler);
                hideModal();
            }
        };
        document.addEventListener('keydown', escHandler);

        document.body.appendChild(overlay);

        loadPage(content);
    }

    function loadPage(content) {
        var api = getApiClient();
        if (!api) {
            content.innerHTML = '<div style="padding:2em;text-align:center;opacity:0.6;">API not available.</div>';
            return;
        }

        api.fetch({
            url: api.getUrl('AniWorld/Page'),
            type: 'GET',
            dataType: 'text'
        }).then(function (html) {
            // Parse HTML and extract the page content
            var parser = new DOMParser();
            var doc = parser.parseFromString(html, 'text/html');
            var pageDiv = doc.querySelector('[data-role="page"]');

            if (pageDiv) {
                content.innerHTML = pageDiv.innerHTML;
            } else {
                content.innerHTML = html;
            }

            // Load the script with a cache-busting parameter so import() always creates a fresh module
            var scriptUrl = api.getUrl('AniWorld/PageScript') + '?_t=' + Date.now();
            import(scriptUrl).then(function (module) {
                if (module.default && typeof module.default === 'function') {
                    module.default(content, { sidebar: true });
                }

                // Always hide settings button in sidebar/non-admin view
                var settingsBtn = content.querySelector('#aw-settings-btn');
                if (settingsBtn) {
                    settingsBtn.style.display = 'none';
                }
            }).catch(function (err) {
                console.error('AniWorld: Failed to load page script:', err);
                content.innerHTML = '<div style="padding:2em;text-align:center;color:#f44336;">Failed to initialize AniWorld script. See console for details.</div>';
            });
        }).catch(function (err) {
            content.innerHTML = '<div style="padding:2em;text-align:center;opacity:0.6;">' +
                'Failed to load AniWorld Downloader. Please check your server permissions.</div>';
            console.error('AniWorld: Failed to load page:', err);
        });
    }

    function hideModal() {
        var overlay = document.getElementById(MODAL_ID);
        if (overlay) {
            // Fire viewhide to clean up polling timers in aniworld.js
            var content = overlay.querySelector('#aw-modal-content');
            if (content) {
                content.dispatchEvent(new Event('viewhide'));
            }
            overlay.remove();
        }

        // Restore hash if needed
        var hash = window.location.hash || '';
        if (hash.indexOf('aniworld') !== -1) {
            if (window.history && window.history.back && window.history.length > 1) {
                window.history.back();
            } else {
                window.location.hash = '#/home';
            }
        }
    }

    // ── URL Hash Navigation Support (e.g. #/aniworld) ──────────────────────────
    function checkHashRoute() {
        var hash = window.location.hash || '';
        if (hash === '#/aniworld' || hash === '#!/aniworld' || hash.startsWith('#/aniworld?')) {
            showModal();
        }
    }

    // ── Master Injector ───────────────────────────────────────────────────────
    function injectAll() {
        injectDesktopTopBar();
        injectUserMenu();
        injectMobileDrawer();
        injectLegacySidebar();
    }

    // Throttled observer for performance
    var scheduled = false;
    function scheduleInject() {
        if (!scheduled) {
            scheduled = true;
            requestAnimationFrame(function () {
                scheduled = false;
                injectAll();
            });
        }
    }

    function setupObserver() {
        var observer = new MutationObserver(scheduleInject);
        observer.observe(document.body, { childList: true, subtree: true });

        window.addEventListener('hashchange', checkHashRoute);
        window.addEventListener('popstate', checkHashRoute);

        // Run initial checks
        injectAll();
        checkHashRoute();
    }

    waitForApiClient(function () {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', setupObserver);
        } else {
            setupObserver();
        }
    });
})();
