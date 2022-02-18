using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Text.RegularExpressions;



namespace SystemDesigner
{
    class CLexer : Lexer
    {
        Dictionary<string, string> defines = new Dictionary<string, string>(); //whenever a define is created it will be added to this  //0.5.1 - change to lexer
        Dictionary<string, object> constants = new Dictionary<string, object>(); //when ever a constant is created it will be added to this //0.5.1 change to lexer
        string inputstring;
        char c = ' ';         //the current char 
        char cnext = ' ';   //the char ahead of the current char
        int charpos = 0;    //the character position in the full file
        int lineCharCount;  //the character position on a line
        int toknPosInLine;  //whenever a token is added toknPosInLine takes on the value of lineCharCount, this way the posn is tagged to the start of the tokn not the end of it
        int lineNumber;     //the line in the file
        Token currTokn;

        public CLexer()
        {
            ClearTokens();
            Console.WriteLine("new C Lexer");
            keywords = new Dictionary<string, TokenType>();
        }
        void error(string s)
        {
            Bugs.AddError("line " + lineNumber + ":" + lineCharCount + " " + s);
        }
        public override List<Token> makeTokens(string line)
        {
            currTokn = new Token(TokenType.tEOF, " ", 0, 0, 0);
            lineCharCount = 1;
            toknPosInLine = lineCharCount;
            lineNumber = 1;
            inputstring = line;
            if (inputstring.Length == 0)    //nothing input so just make it EOF
            {
                currTokn = new Token(TokenType.tEOF, "EOF", 0, 0, 0);
                Tokens.Add(currTokn);
            }

            while (charpos < inputstring.Length)    //while line has chars
            {
                c = inputstring[charpos];
                while (charpos < inputstring.Length-2 && c == ' ') //skip whitespace
                {
                    charpos++;
                    c = inputstring[charpos];
                }
                //digit?
                if (Char.IsDigit(c) //can start with digit 
                    || (charpos < inputstring.Length - 1 && c == '.' && Char.IsDigit(inputstring[charpos + 1])))  //or '.' and digit, 
                {
                    processNumber();
                }

                else if (Char.IsLetter(c) || c == '_' || c == '$' || c == '#') //look for reserved words, variable names and functions names
                {
                    getWordToken();
                }

                else if (c == '"') //look for strings - ignoring escape sequences
                {
                    getStringToken();
                }
                else if (c == '/' && charpos < inputstring.Length - 1 && (inputstring[charpos + 1] == '/' || inputstring[charpos + 1] == '*')) //look for comments, 
                {
                    readComment();
                }
                else if (!Char.IsLetterOrDigit(c)) //look for operations, and escape chars
                {
                    string str = "";
                    Token t = new Token(getToken(inputstring[charpos], out str), str, lineNumber, toknPosInLine, lineCharCount);

                    if (t.Type != TokenType.tLF) //even though LF exist dont include them as they look like they are on the next line
                    {
                        //0.7.4 - annoyed by += -= etc
                        Token last = null;
                        if (Tokens.Count>0)
                            last = Tokens.Last();
                        switch (t.Type)
                        {
                            case TokenType.tAddAssign:
                                Tokens.Add(new Token(TokenType.tAssign, "=", lineNumber, toknPosInLine, lineCharCount));
                                Tokens.Add(new Token(last.Type, last.Value, lineNumber, toknPosInLine, lineCharCount));
                                Tokens.Add(new Token(TokenType.tAdd, "+", lineNumber, toknPosInLine, lineCharCount));
                                break;
                            case TokenType.tSubtractAssign:
                                Tokens.Add(new Token(TokenType.tAssign, "=", lineNumber, toknPosInLine, lineCharCount));
                                Tokens.Add(new Token(last.Type, last.Value, lineNumber, toknPosInLine, lineCharCount));
                                Tokens.Add(new Token(TokenType.tSubtract, "-", lineNumber, toknPosInLine, lineCharCount));
                               break;
                            case TokenType.tMultiplyAssign:
                                Tokens.Add(new Token(TokenType.tAssign, "=", lineNumber, toknPosInLine, lineCharCount));
                                Tokens.Add(new Token(last.Type, last.Value, lineNumber, toknPosInLine, lineCharCount));
                                Tokens.Add(new Token(TokenType.tMultiply, "*", lineNumber, toknPosInLine, lineCharCount));
                                break;
                            case TokenType.tDivideAssign:
                                Tokens.Add(new Token(TokenType.tAssign, "=", lineNumber, toknPosInLine, lineCharCount));
                                Tokens.Add(new Token(last.Type, last.Value, lineNumber, toknPosInLine, lineCharCount));
                                Tokens.Add(new Token(TokenType.tDivide, "/", lineNumber, toknPosInLine, lineCharCount));
                                break;
                            case TokenType.tModulusAssign:
                                Tokens.Add(new Token(TokenType.tAssign, "=", lineNumber, toknPosInLine, lineCharCount));
                                Tokens.Add(new Token(last.Type, last.Value, lineNumber, toknPosInLine, lineCharCount));
                                Tokens.Add(new Token(TokenType.tModulus, "%", lineNumber, toknPosInLine, lineCharCount));
                                break;
                            case TokenType.tBitwiseOrAssign:
                                Tokens.Add(new Token(TokenType.tAssign, "=", lineNumber, toknPosInLine, lineCharCount));
                                Tokens.Add(new Token(last.Type, last.Value, lineNumber, toknPosInLine, lineCharCount));
                                Tokens.Add(new Token(TokenType.tBitwiseOr, "|", lineNumber, toknPosInLine, lineCharCount));
                                break;
                            case TokenType.tBitwiseAndAssign:
                                Tokens.Add(new Token(TokenType.tAssign, "=", lineNumber, toknPosInLine, lineCharCount));
                                Tokens.Add(new Token(last.Type, last.Value, lineNumber, toknPosInLine, lineCharCount));
                                Tokens.Add(new Token(TokenType.tBitwiseAnd, "&", lineNumber, toknPosInLine, lineCharCount));
                                break;
                            case TokenType.tBitwiseExOrAssign:
                                Tokens.Add(new Token(TokenType.tAssign, "=", lineNumber, toknPosInLine, lineCharCount));
                                Tokens.Add(new Token(last.Type, last.Value, lineNumber, toknPosInLine, lineCharCount));
                                Tokens.Add(new Token(TokenType.tBitwiseExOr, "^", lineNumber, toknPosInLine, lineCharCount));
                                break;
                            case TokenType.tLF: //dont add LF otherwise coloring is off, its in the next block
                                break;
                            default:
                                Tokens.Add(t);
                                break;
                        }                     
                    }
                    charpos++;
                    lineCharCount++;
                    toknPosInLine = lineCharCount;

                    if (t.Type == TokenType.tCR) //reset counters for EOL
                    {
                        Tokens.Add(new Token(TokenType.tLF, "LF", lineNumber, toknPosInLine, lineCharCount));
                        lineNumber++;
                        lineCharCount = 0;
                        toknPosInLine = 0;
                        currTokn = t;
                    }
                }
                else //log other unknown chars as for the moment and force EOF??
                {
                    currTokn = new Token(TokenType.tUnknown, c.ToString(), lineNumber, toknPosInLine, lineCharCount);
                    Tokens.Add(currTokn);
                    charpos++;
                    lineCharCount++;
                    toknPosInLine = lineCharCount;
                }
            }
            if (currTokn.Type != TokenType.tCR)
            {
                currTokn = new Token(TokenType.tCR, "CR", lineNumber, toknPosInLine, lineCharCount);
                Tokens.Add(currTokn);
                toknPosInLine = lineCharCount;
            }
            currTokn = new Token(TokenType.tEOF, "EOF", lineNumber, toknPosInLine, lineCharCount);
            Tokens.Add(new Token(TokenType.tCR, " ", lineNumber, 1, 1));
            Tokens.Add(currTokn);
            toknPosInLine = lineCharCount;
            return Tokens;
        }
        public void RemoveCR_EOF() //when parsing a symbol we must not have any CR or EOF in the symbol
        {
            Tokens.RemoveAll(tokn => tokn.Type == TokenType.tCR);
            Tokens.RemoveAll(tokn => tokn.Type == TokenType.tEOF);
        }
        public override void RemoveUnwantedTokens()
        {
            //remove any unwanted tokns
            //http://stackoverflow.com/questions/3069431/listobject-removeall-how-to-create-an-appropriate-predicate
            Tokens.RemoveAll(tokn => tokn.Type == TokenType.tSpace);
            //Tokens.RemoveAll(tokn => tokn.Type == ToknType.tLF);
            Tokens.RemoveAll(tokn => tokn.Type == TokenType.tTab);
            Tokens.RemoveAll(tokn => tokn.Type == TokenType.tComment);
            //Tokens.RemoveAll(tokn => tokn.Type == ToknType.tSemicolon);
            // we dont eat CR as these are needed for the stepping process in the interpreter
            //tokenz.RemoveAll(tokn => tokn._type == ToknType.tCR); 
        }
        TokenType getToken(char c, out string str) //found some operation
        {
            //str = "";
            c = inputstring[charpos];
            try { cnext = inputstring[charpos + 1]; } //look ahead 1 char
            catch { }
            switch (c)
            {
                //case ' ':
                //    str = "sp";
                //    return TokenType.tSpace;
                case '\t':
                    str = "tab";
                    return TokenType.tTab;
                case '\n':
                    str = "LF";
                    return TokenType.tLF;
                case '\r':
                    str = "CR";
                    return TokenType.tCR;
                case ';':
                    str = ";";
                    return TokenType.tSemicolon;
                case ':':
                    str = ":";
                    return TokenType.tColon;
                case ',':
                    str = ",";
                    return TokenType.tComma;
                case '\'':
                    str = "";
                    if (cnext == '\\') // e.g. \r \n etc
                    {
                        //???
                    }
                    else
                    {
                        charpos++;
                        lineCharCount++;
                        str = inputstring[charpos].ToString(); //get the char
                        charpos++; //get the '
                        lineCharCount++;
                        if (inputstring[charpos] == '\'')
                        {
                        }
                    }
                    return TokenType.tCharConst;
                case '=':
                    if (cnext == '=')
                    {
                        charpos++;
                        lineCharCount++;
                        str = "=";
                        return TokenType.tEqual;
                    }
                    else
                        str = "=";
                    return TokenType.tAssign;
                case '+':
                    if (cnext == '+') // ++
                    {
                        charpos++;
                        lineCharCount++;
                        str = "++";
                        if (Tokens.Last().Type == TokenType.tVarIdent || Tokens.Last().Type == TokenType.tRegAddress || Tokens.Last().Type == TokenType.tCloseBracket) //added ')' 0.6.4
                            return TokenType.tPostIncrement;
                        else
                            return TokenType.tPreIncrement;
                    }
                    else if (cnext == '=')   // +=
                    {
                        charpos++;
                        lineCharCount++;
                        str = "+=";
                        return TokenType.tAddAssign;
                    }
                    else
                        str = "+";
                    return TokenType.tAdd; // +
                case '-':
                    if (cnext == '-') //decrement --
                    {
                        charpos++;
                        lineCharCount++;
                        str = "--";
                        if (Tokens.Last().Type == TokenType.tVarIdent || Tokens.Last().Type == TokenType.tRegAddress)
                            return TokenType.tPostDecrement;
                        else
                            return TokenType.tPreDecrement;
                    }
                    else if (cnext == '=') // -= subtract assign
                    {
                        charpos++;
                        lineCharCount++;
                        str = "-=";
                        return TokenType.tSubtractAssign;
                    }
                    else //if (cnext == ' ') // operation - subtraction
                        str = "-";
                    return TokenType.tSubtract;
                //else
                //    break; //ignore as it is part of a number
                case '*': //multiplication or pointer?
                    if (Tokens.Last().Type == TokenType.tType) //declare a pointer
                    {
                        str = "*";
                        return TokenType.tPointer;
                    }
                    else if (Tokens.Last().Type == TokenType.tAssign) //DereferenceOp =*
                    {
                        str = "*";
                        return TokenType.tDereferenceOp;
                    }
                    else if (Tokens.Last().Type == TokenType.tAdd) //DereferenceOp + *
                    {
                        str = "*";
                        return TokenType.tDereferenceOp;
                    }
                    else if (Tokens.Last().Type == TokenType.tAddAssign) //DereferenceOp += *
                    {
                        str = "*";
                        return TokenType.tDereferenceOp;
                    }
                    else if (Tokens.Last().Type == TokenType.tSubtractAssign) //DereferenceOp -= *
                    {
                        str = "*";
                        return TokenType.tDereferenceOp;
                    }
                    else if (Tokens.Last().Type == TokenType.tSubtract) //DereferenceOp - *
                    {
                        str = "*";
                        return TokenType.tDereferenceOp;
                    }
                    else if (Tokens.Last().Type == TokenType.tOpenBracket) //DereferenceOp  (*
                    {
                        str = "*";
                        return TokenType.tDereferenceOp;
                    }
                    else if (Tokens.Last().Type == TokenType.tLF) //DereferenceOp at start of line 
                    {
                        str = "*";
                        return TokenType.tDereferenceOp;
                    }
                    else if (Tokens.Last().Type == TokenType.tDereferenceOp) //DereferenceOp  **
                    {
                        str = "*";
                        return TokenType.tDereferenceOp;
                    }
                    else if (Tokens.Last().Type == TokenType.tReferenceOp) //DereferenceOp  &*
                    {
                        str = "*";
                        return TokenType.tDereferenceOp;
                    }
                    else if (cnext == '=')
                    {
                        charpos++;
                        lineCharCount++;
                        str = "*=";
                        return TokenType.tMultiplyAssign;
                    }
                    else
                        str = "*";
                    return TokenType.tMultiply;
                case '/':                           //divide
                    if (cnext == '=')
                    {
                        charpos++;
                        lineCharCount++;
                        str = "/=";
                        return TokenType.tDivideAssign;
                    }
                    else
                        str = "/";
                    return TokenType.tDivide;
                case '%':
                    if (cnext == '=')
                    {
                        charpos++;
                        lineCharCount++;
                        str = "%=";
                        return TokenType.tModulusAssign;
                    }
                    else
                        str = "%";
                    return TokenType.tModulus;
                case '!':
                    if (cnext == '=')
                    {
                        charpos++;
                        lineCharCount++;
                        str = "!=";
                        return TokenType.tNotEqual;
                    }
                    else
                        str = "!";
                    return TokenType.tLogicalNot;
                case '<':
                    if (cnext == '=')
                    {
                        charpos++;
                        lineCharCount++;
                        str = "<=";
                        return TokenType.tLessEqual;
                    }
                    else if (cnext == '<')
                    {
                        charpos++;
                        lineCharCount++;
                        str = "<<";
                        if (inputstring[charpos + 1] == '=') //0.7.1
                        {
                            charpos++;
                            lineCharCount++;
                            str = "<<=";
                            return TokenType.tShiftLeftAssign;
                        }
                        else
                            return TokenType.tShiftLeft;
                    }
                    else
                        str = "<";
                    return TokenType.tLessThan;
                case '>':
                    if (cnext == '=')
                    {
                        charpos++;
                        lineCharCount++;
                        str = ">=";
                        return TokenType.tGreaterEqual;
                    }
                    else if (cnext == '>')
                    {
                        charpos++;
                        lineCharCount++;
                        str = ">>";
                        if (inputstring[charpos + 1] == '=') //0.7.1
                        {
                            charpos++;
                            lineCharCount++;
                            str = ">>=";
                            return TokenType.tShiftRighttAssign;
                        }
                        else
                            return TokenType.tShiftRight;
                    }
                    else
                        str = ">";
                    return TokenType.tGreaterThan;
                case '|':
                    if (cnext == '|')
                    {
                        charpos++;
                        lineCharCount++;
                        str = "|";
                        return TokenType.tLogicalOr;
                    }
                    else if (cnext == '=')
                    {
                        charpos++;
                        lineCharCount++;
                        str = "|=";
                        return TokenType.tBitwiseOrAssign;
                    }
                    else
                        str = "|";
                    return TokenType.tBitwiseOr;
                case '&':
                    if (Tokens.Last().Type == TokenType.tAssign) //reference operator = &
                    {
                        str = "&";
                        return TokenType.tReferenceOp;
                    }
                    else if (Tokens.Last().Type == TokenType.tPointer) // *&
                    {
                        str = "&";
                        return TokenType.tReferenceOp;
                    }
                    else if (Tokens.Last().Type == TokenType.tComma) // , &
                    {
                        str = "&";
                        return TokenType.tReferenceOp;
                    }
                    else if (Tokens.Last().Type == TokenType.tOpenBracket) // (&
                    {
                        str = "&";
                        return TokenType.tReferenceOp;
                    }
                    else if (Tokens.Last().Type == TokenType.tReferenceOp) //  &&
                    {
                        str = "&";
                        return TokenType.tReferenceOp;
                    }
                    else if (cnext == '&')
                    {
                        charpos++;
                        lineCharCount++;
                        str = "&&";
                        return TokenType.tLogicalAnd;
                    }
                    else if (cnext == '=')
                    {
                        charpos++;
                        lineCharCount++;
                        str = "&=";
                        return TokenType.tBitwiseAndAssign;
                    }
                    else
                        str = "&";
                    return TokenType.tBitwiseAnd;
                case '^':
                    if (cnext == '=')
                    {
                        charpos++;
                        lineCharCount++;
                        str = "^=";
                        return TokenType.tBitwiseExOrAssign;
                    }
                    else
                        str = "^";
                    return TokenType.tBitwiseExOr;
                case '~':
                    str = "~";
                    return TokenType.tBitwiseNot;
                case '(':
                    str = "(";
                    return TokenType.tOpenBracket;
                case ')':
                    str = ")";
                    return TokenType.tCloseBracket;
                case '{':
                    str = "{";
                    return TokenType.tOpenBrace;
                case '}':
                    str = "}";
                    return TokenType.tCloseBrace;
                case '[':
                    str = "[";
                    return TokenType.tOpenSquareBracket;
                case ']':
                    str = "]";
                    return TokenType.tCloseSquareBracket;
                default:
                    str = "EOF";
                    return TokenType.tEOF; //just end
            }
        }
        void getWordToken()//found a word so check if its a reserved word or if its been seen already or its a new word
        {
            string txt = "";
            while (charpos < inputstring.Length && (Char.IsLetterOrDigit(inputstring[charpos]) || inputstring[charpos] == '_' || inputstring[charpos] == '$' || inputstring[charpos] == '#')) //loop until no more digits or _ or $
            {
                txt += inputstring[charpos];
                charpos++;
                lineCharCount++;
            }
            //check when an 'else' is found that its not an 'else if'
            if (txt == "else" && inputstring[charpos] == ' '
                && inputstring[charpos + 1] == 'i'
                && inputstring[charpos + 2] == 'f'
                && (inputstring[charpos + 3] == ' ' || inputstring[charpos + 3] == '('))//we have an else if
            {
                txt = "else if";
                charpos += 3;
            }
            //Console.WriteLine("charpos:" + charpos + " char:'" + inputstring[charpos].ToString() + "'");
            while (charpos < inputstring.Length && inputstring[charpos] == ' ') //eat WS
            {
                lineCharCount++;
                charpos++;
            }

            //0.5.2 to include end of tokn in tokn class
            int toknEndPosInLine = lineCharCount - 2;
            if (toknEndPosInLine < lineCharCount - 2) //fix ocasional -1 length of token
                toknEndPosInLine = lineCharCount;

            if (txt == "signed") //ignore as vars are by default signed
                return;

            if (txt == "unsigned")//0.7.2 - need to get next word and then see if it is a type and change signed to unsigned type
            {
                while (charpos < inputstring.Length && inputstring[charpos] == ' ') //eat WS
                {
                    lineCharCount++;
                    charpos++;
                }
                txt = "";
                while (charpos < inputstring.Length && (Char.IsLetterOrDigit(inputstring[charpos]) || inputstring[charpos] == '_' || inputstring[charpos] == '$' || inputstring[charpos] == '#')) //loop until no more digits or _ or $
                {
                    txt += inputstring[charpos];
                    charpos++;
                    lineCharCount++;
                }
                
                switch (txt)
                {
                    case "char":
                    case "s8":
                    case "int8_t":
                        txt="byte";
                        break;
                    case "int":
                    case "s16":
                    case "int16_t":
                        txt = "u16";
                        break;
                    case "int32_t":
                    case "s32":
                        txt = "u32";
                        break;
                }
            }

            //look for keywords that only require lookups e.g. registers, do, while for etc BUT dont use with odd types 
            if (keywords.ContainsKey(txt))
            {
                Tokens.Add(new Token(keywords[txt], txt, lineNumber, toknPosInLine, toknEndPosInLine));
                return;
            }

            switch (txt) //look for words and types that are processed differently in this language
            {
                case "#define":     //e.g. #define PI 3.14 will become a const, #define setled PORTA|=1<<3 will become a symbol
                    Tokens.Add(new Token(TokenType.tDefine, "define", lineNumber, toknPosInLine, toknEndPosInLine));
                    processDefine();
                    break;
                case "#include":
                    processInclude();
                    break;
                case "const":       //e.g. const int width = 64;
                    Tokens.Add(new Token(TokenType.tConst, "const", lineNumber, toknPosInLine, toknEndPosInLine));
                    processConst();
                    toknPosInLine = lineCharCount;
                    break;
                case "int": Tokens.Add(new Token(TokenType.tType, "int16_t", lineNumber, toknPosInLine, toknEndPosInLine)); toknPosInLine = lineCharCount; break;
                case "char": Tokens.Add(new Token(TokenType.tType, "int8_t", lineNumber, toknPosInLine, toknEndPosInLine)); toknPosInLine = lineCharCount; break;
                case "byte": Tokens.Add(new Token(TokenType.tType, "uint8_t", lineNumber, toknPosInLine, toknEndPosInLine)); toknPosInLine = lineCharCount; break;
                case "u8": Tokens.Add(new Token(TokenType.tType, "uint8_t", lineNumber, toknPosInLine, toknEndPosInLine)); toknPosInLine = lineCharCount; break;
                case "s8": Tokens.Add(new Token(TokenType.tType, "int8_t", lineNumber, toknPosInLine, toknEndPosInLine)); toknPosInLine = lineCharCount; break;
                case "u16": Tokens.Add(new Token(TokenType.tType, "uint16_t", lineNumber, toknPosInLine, toknEndPosInLine)); toknPosInLine = lineCharCount; break;
                case "s16": Tokens.Add(new Token(TokenType.tType, "int16_t", lineNumber, toknPosInLine, toknEndPosInLine)); toknPosInLine = lineCharCount; break;
                case "u32": Tokens.Add(new Token(TokenType.tType, "uint32_t", lineNumber, toknPosInLine, toknEndPosInLine)); toknPosInLine = lineCharCount; break;
                case "s32": Tokens.Add(new Token(TokenType.tType, "int32_t", lineNumber, toknPosInLine, toknEndPosInLine)); toknPosInLine = lineCharCount; break;
                default: //an unrecognised word - will be either var, var.x(struct) , const, define or function identifier 
                    //check to see if it has been added to the defines or constants
                    int temp;
                    charpos = eatWS(charpos);
                    if (defines.ContainsKey(txt)) //already defined
                    {
                        Tokens.Add(new Token(TokenType.tDefineIdent, txt, lineNumber, toknPosInLine, toknEndPosInLine));
                    }
                    else if (constants.ContainsKey(txt)) //already a constant
                    {
                        Tokens.Add(new Token(TokenType.tConstIdent, txt, lineNumber, toknPosInLine, toknEndPosInLine));
                    }
                    else if (charpos < inputstring.Length && inputstring[charpos] == '(') //could be a function or a define e.g. #define setled() ...
                    {
                        charpos = eatWS(charpos);
                        temp = inputstring[charpos+1];
                        if (charpos < inputstring.Length-1 && inputstring[charpos+1] == ')') //e.g. setled() - it might already be in the defines 
                        {
                            if (defines.ContainsKey(txt + "()"))
                            {
                                Tokens.Add(new Token(TokenType.tDefineIdent, txt + "()", lineNumber, toknPosInLine, toknEndPosInLine));
                                charpos += 2;//eat the '(' and ')'
                                break;
                            }
                        }
                        //so it should be a funcprot, funcdecl, funccall, or libCall
                        //func call if last token was not a type (or no last token)
                        //func decl if token after ) is {  or   tCR tLF then {
                        //func prot if token after ) is semicolon or tCR
                        if (Tokens.Count==0 || Tokens.Last().Type != TokenType.tType)
                            Tokens.Add(new Token(TokenType.tFuncCall, txt, lineNumber, toknPosInLine, toknEndPosInLine));
                        else if (Tokens.Count > 0 && Tokens.Last().Type == TokenType.tType) //either funcprot or funcdecl
                        {
                            temp = charpos;                           
                            while (temp < inputstring.Length-3 && inputstring[temp] != ')')     //find the )
                                temp++;
                            temp++;         //character after the )
                            c = inputstring[temp];
                            while (temp < inputstring.Length - 1 && (c=='\n' || c=='\r' || c==' '))  //find the next char after the )
                            {
                                temp++;
                                c=inputstring[temp];
                            }
                            if (c == '/')
                            {
                                while (temp < inputstring.Length - 1 && c !=  '\n') //ignore a comment
                                {
                                    temp++;
                                    c = inputstring[temp];
                                }
                                temp++;
                            }
                            if (temp < inputstring.Length && inputstring[temp] == '{')
                                Tokens.Add(new Token(TokenType.tFuncDecl, txt, lineNumber, toknPosInLine, toknEndPosInLine));
                            else
                                Tokens.Add(new Token(TokenType.tFuncProt, txt, lineNumber, toknPosInLine, toknEndPosInLine));
                        }
                        else
                            error("function is not formatted correctly");
                        toknPosInLine = lineCharCount;
                    }
                    else if (charpos < inputstring.Length && inputstring[charpos] == '.') //var.x struct
                    {
                        txt += '.';
                        charpos++;
                        while (charpos < inputstring.Length && (Char.IsLetterOrDigit(inputstring[charpos]) || inputstring[charpos] == '_' || inputstring[charpos] == '$')) //loop until no more digits or _ or $
                        {
                            txt += inputstring[charpos];
                            charpos++;
                            lineCharCount++;
                        }
                        Tokens.Add(new Token(TokenType.tVarIdent, txt, lineNumber, toknPosInLine, toknEndPosInLine));
                        toknPosInLine = lineCharCount;
                    }
                    else //assume it is a variable name - the parser will pick up if it doesnt exist
                    {
                        Tokens.Add(new Token(TokenType.tVarIdent, txt, lineNumber, toknPosInLine, toknEndPosInLine));
                        toknPosInLine = lineCharCount;
                    }
                    break;
            }
        }
        void getStringToken() //found a " so came here
        {
            TokenType resultToken = TokenType.tStringConst; //default is correctly formatted string constant
            charpos++;
            lineCharCount++;
            string txt = "";
            char aa = inputstring[charpos];
            while (inputstring[charpos] != '"')
            {
                if (charpos < inputstring.Length - 1)
                {
                    txt += inputstring[charpos];
                    charpos++;
                    lineCharCount++;
                }
                else
                {
                    resultToken = TokenType.tStringConstError;
                    break;
                }
            }
            Tokens.Add(new Token(resultToken, txt, lineNumber, toknPosInLine, lineCharCount));
            charpos++;
            lineCharCount++;
            toknPosInLine = lineCharCount;
        }
        void readComment() //found a single / so came here - can pass control commands to the visualizer in the commands
        {
            if (inputstring[charpos + 1] == '/') //look for a second /
            {
                string txt = "";
                int commentpos = charpos+1;
                while (charpos < inputstring.Length && inputstring[charpos] != '\r') //read till end of file or till \r
                {
                    txt += inputstring[charpos];
                    charpos++;
                    lineCharCount++;
                }
                LookforVizCommands(txt, lineNumber, toknPosInLine, lineCharCount);
                lineCharCount++;
                Tokens.Add(new Token(TokenType.tComment, txt, lineNumber, commentpos, lineCharCount));
            }
            else if (inputstring[charpos + 1] == '*') //then look for closing comment */
            {
                string txt = "";
                txt += inputstring[charpos];
                charpos++;
                lineCharCount++;
                txt += inputstring[charpos];
                charpos++;
                lineCharCount++;
                while (charpos < inputstring.Length - 1 && !(inputstring[charpos] == '*' && inputstring[charpos + 1] == '/')) //read till end comment '*/" note this may span several lines
                {
                    if (inputstring[charpos] == '\r')//found newline add token //0.7.4
                    {
                        Tokens.Add(new Token(TokenType.tComment, txt, lineNumber, toknPosInLine, lineCharCount));
                        lineNumber++;
                        txt = "";
                        charpos++;//skip \r
                        //if (inputstring[charpos] == '\n')
                        //    charpos++;
                    }
                    txt += inputstring[charpos];
                    charpos++;
                    lineCharCount++;
                }
                if (charpos < inputstring.Length - 1 && inputstring[charpos] == '*' && inputstring[charpos + 1] == '/')
                {
                    txt += inputstring[charpos];  //get *
                    charpos++;
                    lineCharCount++;
                    txt += inputstring[charpos];  //get /
                    Tokens.Add(new Token(TokenType.tComment, txt, lineNumber, toknPosInLine, lineCharCount));
                    charpos++; //go to next character
                    lineCharCount++;
                    toknPosInLine = lineCharCount;
                }
                else if (charpos < inputstring.Length)
                {
                    Tokens.Add(new Token(TokenType.tCommentError, txt, lineNumber, toknPosInLine, lineCharCount));
                    charpos++; //go to next character
                    lineCharCount++;
                    toknPosInLine = lineCharCount;
                }
            }
        }
        void processNumber()  //found a digit         or  '-' and digit             or  '.' and digit
        {
            //for testing for errors in the number
            bool typeDouble = false; // e
            bool typeDecimal = false; // .
            bool typeFloat = false; //  F

            TokenType resultTokenType = TokenType.tIntConst;
            string nmbrstr = "";
            int dec = 0;

            while (true)//collect the number until we find a reason to break
            {
                if (charpos >= inputstring.Length) break;

                c = inputstring[charpos];
                if (Char.IsLetter(c))
                    c = Char.ToLower(c);  //lowercase any letters e,b,x,f, and errored ones too

                //if its a letter give error however if its e,f, then it will be sorted later on
                //note we dont worry about b or x here they are looked for and removed later on
                if (Char.IsLetter(c) && (!(c == 'e' || c == 'f')))
                {
                    resultTokenType = TokenType.tNumberError;
                    error(" number format error 'e/f'");
                    nmbrstr += inputstring[charpos];
                    charpos++;  //at this stage should we finish the number and start a string? or just error
                    lineCharCount++;
                    //break;
                }
                //else if (c == ',' && Char.IsDigit(inputstring[charpos+1]))//flag error and ignore comma
                //{
                //    error(" Do not use ',' in numbers");
                //    charpos++;  //
                //    lineCharCount++;
                //}
                else if (c == '-') // could be beginning '-' with digit after it or could be '-' after an 'e'
                {
                    if (nmbrstr == "" && c == '-'  //beginning '-'
                        && charpos < inputstring.Length - 1 && (Char.IsDigit(inputstring[charpos + 1]) || inputstring[charpos + 1] == '.')) //must be followed by a digit or '.'
                    {
                        nmbrstr += inputstring[charpos]; //'-'
                        charpos++;
                        lineCharCount++;
                    }
                    else if (charpos > 0 && inputstring[charpos - 1] == 'e' //'e'
                        && c == '-') //followed by '-'
                    {
                        nmbrstr += inputstring[charpos]; //'-'
                        charpos++;
                        lineCharCount++;
                    }
                    else //'-' elsewhere - so that means number followed by subtraction without a space e.g. 34-56;//need to deal with this later at some stage
                    {
                        charpos--;
                        lineCharCount--;
                        break;
                    }
                }

                else if (c == 'f')
                {
                    if (!typeFloat) //havent already got an F so it ok
                    {
                        typeFloat = true;
                        //nmbrstr += "F"; dont add the F
                        resultTokenType = TokenType.tFloatConst;
                        charpos++;
                        lineCharCount++;
                    }
                    else         //already got an f so is an error
                    {
                        resultTokenType = TokenType.tNumberError;
                        error(" number format error 'f'");
                        break;
                    }
                }

                else if (c == '.')
                {
                    if (typeDouble)//cannot have a '.' after an 'e' already been found
                    {
                        resultTokenType = TokenType.tNumberError;
                        error(" number format error 'e.'");
                        break;
                    }
                    if (!typeDecimal) //can only have one dec pt in any number form and this makes it a double
                    {
                        typeDecimal = true;
                        nmbrstr += ".";
                        resultTokenType = TokenType.tFloatConst;
                        charpos++;
                        lineCharCount++;
                    }
                    else         //already got an . so must be an error
                    {
                        resultTokenType = TokenType.tNumberError;
                        error(" number format error '.'");
                        break;
                    }
                }

                else if (inputstring[charpos] == 'e')
                {
                    if (charpos < inputstring.Length - 1)
                        if (!typeDouble && (inputstring[charpos + 1] == '-' || Char.IsDigit(inputstring[charpos + 1]))) //add but next char must be a '-' or digit ?????!typedouble
                        {
                            typeDouble = true;
                            nmbrstr += "e";
                            resultTokenType = TokenType.tFloatConst;
                            charpos++;
                            lineCharCount++;
                        }
                        else         //already got an e so must be an error
                        {
                            resultTokenType = TokenType.tNumberError;
                            error(" number format error1 'e'");
                            nmbrstr += "e";
                            break;
                        }
                    else
                    {
                        resultTokenType = TokenType.tNumberError;
                        error(" number format error2 'e'");
                        nmbrstr += "e";
                        break;
                    }

                }

                else if (Char.IsDigit(inputstring[charpos]))
                {
                    if (inputstring[charpos] == '0' && nmbrstr == "") //found a first 0 maybe we will find a hex or bin number e.g. 0xaf or 0b10101010
                    {
                        if (charpos < inputstring.Length - 2 && (inputstring[charpos + 1] == 'x' || inputstring[charpos + 1] == 'X'))//0x + at least 1 digit
                        {
                            charpos += 2; //ignore 0x
                            while (charpos < inputstring.Length && Uri.IsHexDigit(inputstring[charpos]))
                            {

                                nmbrstr += inputstring[charpos];
                                charpos++;
                                lineCharCount++;
                            }
                            //convert hex to dec and decide what sort of unsigned type it will be
                            //dec = Convert.ToInt32(nmbrstr, 16);
                            //http://stackoverflow.com/questions/98559/how-to-parse-hex-values-into-a-uint
                            bool fail = Int32.TryParse(nmbrstr, System.Globalization.NumberStyles.HexNumber, null, out dec);
                            nmbrstr = dec.ToString();

                        }
                        else if (charpos < inputstring.Length - 2 && (inputstring[charpos + 1] == 'b' || inputstring[charpos + 1] == 'B'))//0b + at least 1 digit
                        {
                            charpos += 2; //ignore 0b - add it at end if numbererror
                            while (charpos < inputstring.Length && (inputstring[charpos] == '0' || inputstring[charpos] == '1')) //collect 0's,1's
                            {
                                if (inputstring[charpos] == '0' || inputstring[charpos] == '1')
                                {
                                    nmbrstr += inputstring[charpos];
                                }
                                charpos++;
                                lineCharCount++;
                            }
                            if (charpos == inputstring.Length || inputstring[charpos] == ' ' || inputstring[charpos] == ';' || inputstring[charpos] == ')' || inputstring[charpos] == '\r' || inputstring[charpos] == '/') //end if nochars or space or ; or \r 
                            {
                                dec = Convert.ToInt32(nmbrstr, 2); // decide what sort of unsigned type it will be
                                nmbrstr = dec.ToString();
                                //*********if (dec > 255) resultToken = ToknType.t16bitConst;
                                //*********if (dec > 65535) resultToken = ToknType.t32bitConst;
                            }
                            else //unexpected char at end of 0b11111111
                            {
                                resultTokenType = TokenType.tNumberError;
                                error(" number format error 'binary'");
                                nmbrstr = "0b" + nmbrstr + inputstring[charpos];
                                break;
                            }
                        }
                        else //is a 0 not followed by a x or b 
                        {
                            nmbrstr += inputstring[charpos];
                            charpos++;
                            lineCharCount++;
                            if (resultTokenType != TokenType.tNumberError)
                            {
                                dec = Convert.ToInt32(nmbrstr);
                                //if (dec > 255 || dec < -127) resultToken = ToknType.t16bitConst;
                                //if (dec > 65535 || dec < -32767) resultToken = ToknType.t32bitConst;
                            }
                        }
                    }
                    else //digit 0 to 9 - just add to the number
                    {
                        nmbrstr += inputstring[charpos];
                        charpos++;
                        lineCharCount++;
                    }
                }
                else //found something else so step backwards as we will step forwards at the end of this loop and we dont want to loose this char
                {
                    charpos--;
                    lineCharCount--;
                    break;
                }
            }
            //if (nmbrstr[nmbrstr.Length - 1] == 'x') {resultTokenType = ToknType.tNumberError; error(" number format error3 'x'");} //if last char is x badly formed number
            //if (nmbrstr[nmbrstr.Length - 1] == 'b') {resultTokenType = ToknType.tNumberError; error(" number format error3 'b'");}//if last char is b badly formed number
            //if (nmbrstr[nmbrstr.Length - 1] == 'e') {resultTokenType = ToknType.tNumberError; error(" number format error3 'e'");}//if last char is e badly formed number
            //if (nmbrstr[nmbrstr.Length - 1] == '.') { resultTokenType = ToknType.tNumberError; error(" number format error3 '.'");}//if last char is . badly formed number
            currTokn = new Token(resultTokenType, nmbrstr, lineNumber, toknPosInLine, lineCharCount);
            Tokens.Add(currTokn);
            charpos++;
            lineCharCount++;
            toknPosInLine = lineCharCount;
        }
        int eatWS(int pos)
        {
            while (pos < inputstring.Length && c == ' ') //eat WS
            {
                c = inputstring[pos];
                if (c != ' ') //0.7.4
                    return pos;
                lineCharCount++;
                pos++;
            }
            return pos;
        }
        int findlfOrSemiOrBrace(int pos)
        {
            c = inputstring[pos];
            while (pos < inputstring.Length - 1 && c != ';' && c != '\n' && c != '{') //eat WS
            {
                //lineCharCount++;
                pos++;
                c = inputstring[pos];                
            }
            return pos;
        }
        void processDefine()
        {
            string name = "";
            int definetoknPosInLine;
            string symb = "";
            // bool commentflag = false;
            c = inputstring[charpos];
            charpos++;
            charpos = eatWS(charpos);

            toknPosInLine = lineCharCount;
            while (charpos < inputstring.Length && c != ' ')
            {
                name += c;
                c = inputstring[charpos];
                charpos++;
                lineCharCount++;
            }
            //Tokens.Add(new Tokn(ToknType.tDefineIdent, name, lineNumber, toknPosInLine, lineCharCount));
            definetoknPosInLine = toknPosInLine;
            //getthe symbol
            charpos = eatWS(charpos);
            toknPosInLine = lineCharCount;
            //read till CR or comment starts
            if (c == '=')//oops = with define, error user and ignore
            {
                error(" dont use '=' with defines");
                c = inputstring[charpos];
                charpos++;
                lineCharCount++;
            }
            while (charpos < inputstring.Length && !(c == '\r' )) //|| c == '/')) //0.7.2 oops ignores tDivide as it 
            {
                if (c == ';')//oops ; with define
                    error(" dont use ';' with defines");
                else
                    symb += c;
                c = inputstring[charpos];
                charpos++;
                lineCharCount++;
                c = inputstring[charpos];
                //    if (c == '/') //comment starts so insert CR
                //        commentflag = true;
            }
            charpos = eatWS(charpos);
            while (charpos < inputstring.Length && c != '\n') //read till LF
            {
                c = inputstring[charpos];
                charpos++;
                lineCharCount++;
            }
            //check to see if text or a number otherwise it will be a string of tokens
            long longvalue = 0;
            float floatval;
            if (symb.Contains("0x"))//0.7.2 for hex numbers
            {
                symb = symb.Remove(0, 2);//assume it starts with 0x and no other combination
                int iNumber = int.Parse(symb, System.Globalization.NumberStyles.HexNumber);
                symb = iNumber.ToString();
            }
            if (symb.Contains("0b"))//0.7.2 for hex numbers
            {
                symb = symb.Remove(0, 2);//assume it starts with 0x and no other combination
                symb = Convert.ToInt32(symb, 2).ToString();
                //int iNumber = int.Parse(symb, System.Globalization.NumberStyles.num);
                //symb = b.ToString();
            }
            if (Int64.TryParse(symb, out longvalue))
            {
                Tokens.Add(new Token(TokenType.tConstIdent, name, lineNumber, definetoknPosInLine, toknPosInLine - 2));  //0.5.1 change
                Tokens.Add(new Token(TokenType.tIntConst, symb, lineNumber, toknPosInLine, lineCharCount));
                constants.Add(name, symb);
            }
            else if (Single.TryParse(symb, out floatval))
            {
                Tokens.Add(new Token(TokenType.tConstIdent, name, lineNumber, definetoknPosInLine, toknPosInLine - 2)); //0.5.1 change
                Tokens.Add(new Token(TokenType.tFloatConst, symb, lineNumber, toknPosInLine, lineCharCount));
                constants.Add(name, symb);
            }
            else if (symb.Contains('"'))   // e.g.   #define message "hello world"
            {
                Tokens.Add(new Token(TokenType.tConstIdent, name, lineNumber, definetoknPosInLine, toknPosInLine - 2)); //0.5.1 change
                Tokens.Add(new Token(TokenType.tStringConst, symb, lineNumber, toknPosInLine, lineCharCount));
                constants.Add(name, symb);
            }
            else
            {
                Tokens.Add(new Token(TokenType.tDefineIdent, name, lineNumber, definetoknPosInLine, toknPosInLine - 2)); //0.5.1 change
                Tokens.Add(new Token(TokenType.tStringOfTokens, symb, lineNumber, toknPosInLine, lineCharCount - 2));
                defines.Add(name, symb);
            }

            Tokens.Add(new Token(TokenType.tCR, "CR", lineNumber, lineCharCount, lineCharCount));
            Tokens.Add(new Token(TokenType.tLF, "LF", lineNumber, lineCharCount, lineCharCount));
            lineCharCount = toknPosInLine = 1;
            lineNumber++;
            //if (commentflag )

        }
        void processInclude()
        {
            string file = "";
            //c = inputstring[charpos]; //removes the '#'
            charpos++; //removes the '"'
            charpos = eatWS(charpos); //eat ws between #include and beginning of file
            //read till comment starts
            while (true)
            {
                if (charpos >= inputstring.Length)
                    break;
                c = inputstring[charpos];
                if (charpos < inputstring.Length - 1 && c == '/' && (inputstring[charpos + 1] == '/' || inputstring[charpos + 1] == '*'))
                {
                    break;
                }
                if (charpos < inputstring.Length && c == '\r')
                    break;
                if (charpos < inputstring.Length && c == '"')
                {
                    charpos++;
                    c = inputstring[charpos];
                    charpos = eatWS(charpos);
                    break;
                }
                if (charpos < inputstring.Length && c == '>')
                {
                    charpos++;
                    c = inputstring[charpos];
                    charpos = eatWS(charpos);
                    break;
                }
                file += c;
                charpos++;
                lineCharCount++;

            }
            Tokens.Add(new Token(TokenType.tInclude, file, lineNumber, toknPosInLine, lineCharCount + 1));
            c = inputstring[charpos];
            char c1 = inputstring[charpos + 1];
            if (charpos < inputstring.Length - 1 && c == '/' && (inputstring[charpos + 1] == '/' || inputstring[charpos + 1] == '*'))
            {
                toknPosInLine = lineCharCount;
                readComment();
                return; //0.7.4
            }
            charpos = eatWS(charpos);
            if (charpos < inputstring.Length - 1 && c == '\r') //read till end of line
            {
                charpos++;
                c = inputstring[charpos];
                Tokens.Add(new Token(TokenType.tCR, "CR", lineNumber, lineCharCount - 1, lineCharCount)); //reinsert eaten CR
            }
            if (charpos < inputstring.Length - 1 && c == '\n') //read till end of line
            {
                charpos++;
                c = inputstring[charpos];
                Tokens.Add(new Token(TokenType.tLF, "LF", lineNumber, lineCharCount, lineCharCount)); //reinsert eaten LF
            }
            //charpos--; //go back 1
            //IncludesTable.Instance.AddSymbol(name, ToknType.tStringConst, file, 0, false); //do this?????????????????????????
            lineCharCount = toknPosInLine = 1;
            lineNumber++;
        }
        void processConst()
        {
            string type = "";
            string name = "";
            string value = "";
            c = inputstring[charpos];
            charpos++;
            charpos = eatWS(charpos);

            //get the type
            toknPosInLine = lineCharCount;
            while (charpos < inputstring.Length && c != ' ')
            {
                type += c;
                c = inputstring[charpos];
                charpos++;
                lineCharCount++;
            }
            if (type == "signed") //ignore
            {
                type = "";
                charpos = eatWS(charpos);
                while (charpos < inputstring.Length && c != ' ')
                {
                    type += c;
                    c = inputstring[charpos];
                    charpos++;
                    lineCharCount++;
                }
            }
            if (type == "unsigned") //not really interested in unsigned constants - we can just leave them
            {
                type = "u";
                charpos = eatWS(charpos);
                while (charpos < inputstring.Length && c != ' ')
                {
                    type += c;
                    c = inputstring[charpos];
                    charpos++;
                    lineCharCount++;
                }

            }
            Tokens.Add(new Token(TokenType.tType, type, lineNumber, toknPosInLine, lineCharCount - 2));

            //get the name
            toknPosInLine = lineCharCount;
            charpos = eatWS(charpos);
            while (charpos < inputstring.Length && c != ' ' && c != '=')
            {
                name += c;
                c = inputstring[charpos];
                charpos++;
                lineCharCount++;
            }
            Tokens.Add(new Token(TokenType.tConstIdent, name, lineNumber, toknPosInLine, lineCharCount));

            //get the '='
            toknPosInLine = lineCharCount;
            charpos = eatWS(charpos);
            if (c == '=')
            {
                Tokens.Add(new Token(TokenType.tAssign, "=", lineNumber, toknPosInLine, lineCharCount));
                c = inputstring[charpos];
                charpos++;
                lineCharCount++;
            }
            else
                error(" expected to fnd a '=' here");

            //get the value 
            toknPosInLine = lineCharCount;
            charpos = eatWS(charpos);
            while (charpos < inputstring.Length && (!(c == '\r' || c == '/'))) //till CR or comment starts
            {
                if (c != ';')
                    value += c;
                c = inputstring[charpos];
                charpos++;
                lineCharCount++;
            }
            charpos = eatWS(charpos);
            while (charpos < inputstring.Length && c != '\n') //read till LF
            {
                c = inputstring[charpos];
                charpos++;
                lineCharCount++;
            }

            //check to see if text or a number
            long longvalue = 0;
            float floatval;
            if (Int64.TryParse(value, out longvalue))
            {
                //ConstantsTable.Instance.AddConstant(name, type, "", longvalue, 0, false);
                Tokens.Add(new Token(TokenType.tIntConst, value, lineNumber, toknPosInLine, lineCharCount));
            }
            else if (Single.TryParse(value, out floatval))
            {
                //ConstantsTable.Instance.AddConstant(name, type, "", 0, floatval, false);
                Tokens.Add(new Token(TokenType.tFloatConst, value, lineNumber, toknPosInLine, lineCharCount));
            }
            else
            {
                //SymbolTable.Instance.AddSymbol(name, ToknType.tStringConst, value, 0, false);
                Tokens.Add(new Token(TokenType.tStringConst, value, lineNumber, toknPosInLine, lineCharCount));
            }
            object o;
            if (constants.TryGetValue(name, out o)) //already exists
            {
                error("constant name already used");
            }
            else
            {
                constants.Add(name, value);
            }
            Tokens.Add(new Token(TokenType.tCR, "CR", lineNumber, lineCharCount - 1, lineCharCount - 1)); //reinsert eaten CR
            Tokens.Add(new Token(TokenType.tLF, "LF", lineNumber, lineCharCount, lineCharCount)); //reinsert eaten LF
            lineCharCount = toknPosInLine = 1;
            lineNumber++;
        }
        public string[] getDefines()
        {
            return defines.Keys.ToArray();
        }
        public string[] getConstants()
        {
            return constants.Keys.ToArray();
        }
        public override string ToString()
        {
            string outstr = "";
            int index = 0;
            foreach (Token t in Tokens)
            {
                outstr += index + " " + t.ToString() + Environment.NewLine;
                index++;
            }
            return outstr;
        }
    }
}
