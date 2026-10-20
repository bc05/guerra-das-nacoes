using System;
using System.Collections.Generic;

namespace GuerraDasNacoes
{
    // Additive content only: no existing item, spawn, wallet, revision or administrator value is replaced.
    public static class CatacombsContent
    {
        public static void Add(EconomyConfig config)
        {
            // Drafts may be saved before validation. Reject incomplete inputs before mutating
            // them so installation can keep the original draft and publish valid active content.
            if(config==null || config.items==null || config.monsters==null || config.monsterSpecies==null ||
                config.monsterSpawns==null || config.lootTables==null)
                throw new ArgumentException("Coleções obrigatórias ausentes na configuração das Catacumbas.",nameof(config));
            if(Array.Exists(config.items,item=>item==null) || Array.Exists(config.monsters,monster=>monster==null) ||
                Array.Exists(config.monsterSpecies,species=>species==null) || Array.Exists(config.monsterSpawns,spawn=>spawn==null) ||
                Array.Exists(config.lootTables,table=>table==null))
                throw new ArgumentException("A configuração das Catacumbas contém registros nulos.",nameof(config));
            MonsterCatalog.Upgrade(config);
            var species=new List<EconomyMonsterSpecies>(config.monsterSpecies);
            var definitions=new[] {
                Species("cat-skeleton","Esqueleto do Ossuário",16,3000,38,950,"skeleton","melee"),
                Species("cat-conjurer","Conjurador das Cinzas",20,3400,48,1200,"skeleton-caster","ranged"),
                Species("cat-guardian","Guardião da Cripta",23,5200,55,1600,"skeleton","melee")
            };
            definitions[2].elite=true;definitions[2].visualScale=1.12f;
            foreach(var entry in definitions)if(!species.Exists(s=>s.id==entry.id))species.Add(entry);
            config.monsterSpecies=species.ToArray();
            var spawns=new List<EconomyMonsterSpawn>(config.monsterSpawns);
            string[] ids={"cat-skeleton-0","cat-skeleton-1","cat-skeleton-2","cat-conjurer-0","cat-conjurer-1","cat-guardian-0","cat-conjurer-2"};
            string[] kinds={"cat-skeleton","cat-skeleton","cat-skeleton","cat-conjurer","cat-conjurer","cat-guardian","cat-conjurer"};
            for(int i=0;i<ids.Length;i++)
                if(!spawns.Exists(s=>s.id==ids[i]))spawns.Add(new EconomyMonsterSpawn {
                    id=ids[i],speciesId=kinds[i],mapId=CatacombsLayout.MapId,x=CatacombsLayout.SpawnPoints[i].X,z=CatacombsLayout.SpawnPoints[i].Z });
            config.monsterSpawns=spawns.ToArray();
            var tables=new List<EconomyLootTable>(config.lootTables);
            foreach(var entry in new[] {
                Table(config,"loot-cat-skeleton",55,85,100000,new[]{"arcane-hands","arcane-feet"}),
                Table(config,"loot-cat-conjurer",70,110,110000,new[]{"arcane-head","arcane-necklace"}),
                Table(config,"loot-cat-guardian",95,140,140000,new[]{"arcane-chest","arcane-shield"})
            })if(!tables.Exists(t=>t.id==entry.id))tables.Add(entry);
            config.lootTables=tables.ToArray();
        }

        private static EconomyMonsterSpecies Species(string id,string name,int level,int hp,int damage,int xp,string visual,string behavior) => new EconomyMonsterSpecies {
            id=id,name=name,level=level,health=hp,damage=damage,experience=xp,visualKind=visual,behavior=behavior,
            lootTableId=id=="cat-conjurer"?"loot-cat-conjurer":id=="cat-guardian"?"loot-cat-guardian":"loot-cat-skeleton",
            aggroRadius=5,leashRadius=12,moveSpeed=2.15f,attackRange=behavior=="ranged"?8f:1.65f,
            attackPreparation=behavior=="ranged"?1.3f:.8f,attackInterval=behavior=="ranged"?2.6f:1.6f,
            areaRadius=behavior=="ranged"?1.4f:3,respawnDelay=12,visualScale=1 };

        private static EconomyLootTable Table(EconomyConfig config,string id,long min,long max,int chance,string[] items)
        {
            var entries=new List<EconomyLootEntry>();
            foreach(string wanted in items)
            {
                var item=Array.Find(config.items,i=>i.definitionId==wanted&&i.enabled&&i.minSwordProficiency<=1);
                if(item!=null)entries.Add(new EconomyLootEntry{definitionId=item.definitionId,definitionRevision=item.definitionRevision,weight=1});
            }
            return new EconomyLootTable {
            id=id,coinsMin=min,coinsMax=max,scrapChancePpm=600000,scrapMin=2,scrapMax=5,
            groups=entries.Count==0?new EconomyLootGroup[0]:new[]{new EconomyLootGroup{id="runico",chancePpm=chance,entries=entries.ToArray()}} };
        }
    }
}
