# Rirogan Desktop

Cliente do Rirogan para Windows x64, com launcher, acesso ao chat e às chamadas, notificações na bandeja e atualizações por arquivo. O aplicativo usa o serviço em [rirogan.com](https://rirogan.com/) e consulta o canal de versões em [software.rirogan.com](https://software.rirogan.com/). Este repositório reúne o código do cliente, do launcher e os scripts de compilação e distribuição.

## Compilar

No Windows, instale o SDK .NET 10 e o Inno Setup 6. Na raiz do repositório, execute:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-windows.ps1
```

Compile `installer\Rirogan.iss` no Inno Setup. Para preparar os arquivos de publicação, execute:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\generate-update-manifest.ps1
```

O instalador fica em `dist\installer\`. O pacote para o subdomínio fica em `dist\site\`. O workflow **Build Windows** compila o mesmo projeto no GitHub Actions e disponibiliza os arquivos como artefato da execução.

## Dependências

O cliente é feito em .NET e Windows Forms. O SDK WebView2 é obtido do NuGet na compilação e suas bibliotecas redistribuíveis da Microsoft acompanham o cliente. O WebView2 Runtime é instalado separadamente no Windows. As dependências de terceiros seguem suas próprias licenças. [Distribuição do WebView2](https://learn.microsoft.com/pt-br/microsoft-edge/webview2/concepts/distribution)

## Code signing policy

O workflow atual gera builds sem assinatura do Rirogan. Uma integração com a SignPath Foundation está em preparação.

O launcher consulta o canal de versões antes de abrir o cliente. O cliente usa o WebView2 para acessar os serviços do Rirogan. O recurso opcional de detecção de jogos vem desativado por padrão e pode ser controlado nas preferências. [Política de Privacidade](https://rirogan.com/privacidade.php)
