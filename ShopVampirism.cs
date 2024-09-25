using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ShopAPI;

namespace ShopVampirism
{
    public class ShopVampirism : BasePlugin
    {
        public override string ModuleName => "[SHOP] Vampirism";
        public override string ModuleDescription => "";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.1";

        private IShopApi? SHOP_API;
        private const string CategoryName = "Vampirism";
        public static JObject? JsonVampirism { get; private set; }
        private readonly PlayerVampirism[] playerVampirisms = new PlayerVampirism[65];

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            SHOP_API = IShopApi.Capability.Get();
            if (SHOP_API == null) return;

            LoadConfig();
            InitializeShopItems();
            SetupTimersAndListeners();
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(ModuleDirectory, "../../configs/plugins/Shop/Vampirism.json");
            if (File.Exists(configPath))
            {
                JsonVampirism = JObject.Parse(File.ReadAllText(configPath));
            }
        }

        private void InitializeShopItems()
        {
            if (JsonVampirism == null || SHOP_API == null) return;

            SHOP_API.CreateCategory(CategoryName, "Вампиризм");

            foreach (var item in JsonVampirism.Properties().Where(p => p.Value is JObject))
            {
                Task.Run(async () =>
                {
                    int itemId = await SHOP_API.AddItem(
                        item.Name,
                        (string)item.Value["name"]!,
                        CategoryName,
                        (int)item.Value["price"]!,
                        (int)item.Value["sellprice"]!,
                        (int)item.Value["duration"]!
                    );
                    SHOP_API.SetItemCallbacks(itemId, OnClientBuyItem, OnClientSellItem, OnClientToggleItem);
                }).Wait();
            }
        }

        private void SetupTimersAndListeners()
        {
            RegisterListener<Listeners.OnClientDisconnect>(playerSlot => playerVampirisms[playerSlot] = null!);

            RegisterEventHandler<EventPlayerHurt>((@event, _) =>
            {
                var attacker = @event.Attacker;

                if (attacker is null || !attacker.IsValid || playerVampirisms[attacker.Slot] == null)
                    return HookResult.Continue;

                if (attacker == @event.Userid) return HookResult.Continue;

                if (attacker.PawnIsAlive)
                {
                    var attackerPawn = attacker.PlayerPawn.Value;
                    if (attackerPawn == null) return HookResult.Continue;

                    var health = attackerPawn.Health +
                                 (int)float.Round(@event.DmgHealth * playerVampirisms[attacker.Slot].VampirismPercent /
                                                 100.0f);

                    if (health > attackerPawn.MaxHealth)
                        health = attackerPawn.MaxHealth;

                    attackerPawn.Health = health;
                    Utilities.SetStateChanged(attackerPawn, "CBaseEntity", "m_iHealth");
                }

                return HookResult.Continue;
            });
        }

        public HookResult OnClientBuyItem(CCSPlayerController player, int itemId, string categoryName, string uniqueName,
            int buyPrice, int sellPrice, int duration, int count)
        {
            if (TryGetVampirismPercent(uniqueName, out float vampirismPercent))
            {
                playerVampirisms[player.Slot] = new PlayerVampirism(vampirismPercent, itemId);
            }
            else
            {
                Logger.LogError($"{uniqueName} has invalid or missing 'vampirismpercent' in config!");
            }
            return HookResult.Continue;
        }

        public HookResult OnClientToggleItem(CCSPlayerController player, int itemId, string uniqueName, int state)
        {
            if (state == 1 && TryGetVampirismPercent(uniqueName, out float vampirismPercent))
            {
                playerVampirisms[player.Slot] = new PlayerVampirism(vampirismPercent, itemId);
            }
            else if (state == 0)
            {
                OnClientSellItem(player, itemId, uniqueName, 0);
            }
            return HookResult.Continue;
        }

        public HookResult OnClientSellItem(CCSPlayerController player, int itemId, string uniqueName, int sellPrice)
        {
            playerVampirisms[player.Slot] = null!;
            return HookResult.Continue;
        }

        private bool TryGetVampirismPercent(string uniqueName, out float vampirismPercent)
        {
            vampirismPercent = 0f;
            if (JsonVampirism != null && JsonVampirism.TryGetValue(uniqueName, out var obj) &&
                obj is JObject jsonItem && jsonItem["vampirismpercent"] != null &&
                jsonItem["vampirismpercent"]!.Type != JTokenType.Null)
            {
                vampirismPercent = float.Parse(jsonItem["vampirismpercent"]!.ToString());
                return true;
            }

            return false;
        }

        public record class PlayerVampirism(float VampirismPercent, int ItemID);
    }
}
