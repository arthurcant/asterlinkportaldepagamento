# Velocidade por plano no HotSpot

Novos pedidos guardam `SpeedMbps` do banco e `EnforceSpeedLimit=true` no documento
JSON de `network_orders`. Não é necessária uma coluna ou migração SQL adicional.
A velocidade recebida do navegador não é utilizada. Valores menores ou iguais
a zero impedem iniciar uma cobrança.

| Plano | Upload / download | Tempo de uso |
| --- | --- | --- |
| Básico | 10 / 10 Mbps | 30 minutos |
| Essencial | 20 / 20 Mbps | 60 minutos |
| Plus | 30 / 30 Mbps | 120 minutos |
| Premium | 50 / 50 Mbps | 240 minutos |

## Provisionamento

Somente após confirmar um pagamento aprovado, a aplicação procura um perfil
`<Network:UserProfile>-aster-v1-<velocidade>m`. Se ainda não existir, usa REST
`PUT /rest/ip/hotspot/user/profile` com `copy-from` apontando para o perfil base,
`shared-users=1` e `rate-limit`, por exemplo `20M/20M`.

O perfil base precisa existir com `shared-users=1`. Suas configurações são
copiadas, não sobrescritas. O perfil derivado é reutilizado; não é recriado em
cada compra. Alterações posteriores no perfil base não se propagam às cópias.
O responsável pela rede deve revisar também scripts on-login/on-logout e filas
herdados do perfil base. Nenhuma configuração de firewall é alterada pelo código.

Uma nova consulta confirma a velocidade antes de cadastrar o usuário, inclusive
quando a resposta da criação se perde. Perfil existente com limite divergente
gera erro para revisão, em vez de ser sobrescrito ou liberar acesso sem limite.
O usuário recebe esse perfil e mantém seu `limit-uptime` original. O login no
HotSpot gera a fila dinâmica que aplica a velocidade. Crédito, débito e Pix
usam o mesmo fluxo de provisionamento após aprovação.

Pedidos históricos sem `EnforceSpeedLimit=true` continuam usando o perfil
anterior. Não são migrados ou desconectados automaticamente. Para testar a
funcionalidade, gere um pedido novo, não reutilize um Pix/pedido antigo.

## Verificação no terminal RouterOS

Após um novo pagamento aprovado, consulte (substitua ID_DO_PEDIDO):

```text
/ip hotspot user print detail where name="aster-ID_DO_PEDIDO"
/ip hotspot user profile print detail where name~"aster-v1-"
/ip hotspot active print detail where user="aster-ID_DO_PEDIDO"
/queue simple print detail where dynamic=yes
```

Confira o perfil no usuário, `rate-limit` no perfil e a fila dinâmica com o IP
da sessão e o limite correspondente. São limites máximos, não banda garantida.
O tráfego precisa atravessar o HotSpot; navegar pela internet física do Windows
sem passar pelo CHR não mede esses limites. O especialista de rede deve garantir
que FastTrack não desvie o tráfego dessas filas.

A licença gratuita do CHR limita a transmissão a 1 Mbps por interface. Ela
permite conferir o provisionamento, mas não medir 10–50 Mbps efetivos. Não foi
alterada nenhuma licença pelo projeto.

## Testes

```text
dotnet run -c Release --project tests/NetworkIntegration/Network.Tests.csproj -- --speed-only
dotnet run -c Release --project tests/NetworkIntegration/Network.Tests.csproj
```

O primeiro comando verifica limites, retries, perfis divergentes e o fluxo de
pagamento por cartão com HTTP simulado. O segundo executa toda a suíte. Nenhum
desses comandos faz cobranças nem modifica um roteador real.

Referências oficiais:
- https://manual.mikrotik.com/docs/authentication-authorization-accounting/hotspot-captive-portal/
- https://manual.mikrotik.com/docs/developer-guides/rest-api/
- https://manual.mikrotik.com/docs/developer-guides/scripting/
- https://help.mikrotik.com/docs/spaces/ROS/pages/328088/Queues
- https://help.mikrotik.com/docs/spaces/ROS/pages/18350234/Cloud+Hosted+Router+CHR
